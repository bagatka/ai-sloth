using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Harnesses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Chats.Harness;

// Talks to one chat's agent over ACP, through its process's lines: starts it, hands it messages,
// answers its requests, and saves what it reports. ChatRunners keeps at most one runner per chat. A
// runner exits when its chat is idle; how far the agent's output was read is saved with every change,
// so the next runner, even after a restart, continues exactly there.
internal sealed class ChatRunner(
    ChatId chatId,
    IDbContextFactory<ChatsDbContext> databases,
    IServiceScopeFactory scopes,
    AgentProcess agent,
    ChatsSettings settings,
    ChatSignals signals,
    TimeProvider time,
    ILogger logger)
{
    private const int MaxBatch = 200;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    private readonly Channel<RunnerInput> _inputs = Channel.CreateBounded<RunnerInput>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    // The agent answered a steer with promptRequired: its turn is over even if its answer to the prompt
    // isn't read yet, so nothing more is offered to it until the turn ends here too.
    private bool _agentFinishedTurn;
    private int _stopRequested;

    public ChatId ChatId => chatId;

    public bool HasInput => _inputs.Reader.Count > 0 || Volatile.Read(ref _stopRequested) == 1;

    // Losing a nudge to a full queue is fine: the runner looks at the chat after every input.
    public void Wake()
    {
        _inputs.Writer.TryWrite(new RunnerInput(new WakeUp()));
    }

    public void Stop()
    {
        Interlocked.Exchange(ref _stopRequested, 1);
        Wake();
    }

    // Runs until the chat is idle and retire agrees, or until ct is cancelled. A failure is logged and
    // retried, reading the agent's output again from the last saved offset.
    public async Task RunAsync(Func<ChatRunner, bool> retire, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                Progress progress = await AdvanceAsync(ct);
                bool retired;
                if (progress.Harness is not null)
                {
                    retired = await ServeAsync(progress, progress.Harness.Value, retire, ct);
                }
                else
                {
                    retired = false;
                    if (progress.Idle)
                    {
                        retired = retire(this);
                    }

                    if (!retired)
                    {
                        List<RunnerInput> batch = await NextBatchAsync(ct);
                        await HandleAsync(batch, ct);
                    }
                }

                if (retired)
                {
                    return;
                }
            }
            catch (Exception exception) when (!ct.IsCancellationRequested)
            {
                Log.RunnerFailed(logger, exception, chatId.Value);

                // Lines already queued were read past the saved offset; the next reader reads them again.
                while (_inputs.Reader.TryRead(out _))
                {
                }

                await Task.Delay(RetryDelay, time, ct);
            }
        }
    }

    // Works with one agent process, reading its output, until the process ends or is replaced, or the
    // chat is idle and the runner retires; returns whether it retired.
    private async Task<bool> ServeAsync(Progress progress, ProcessId process, Func<ChatRunner, bool> retire, CancellationToken ct)
    {
        NookId nookId = progress.NookId;
        long offset = progress.OutputOffset;
        await using Reading reading = new Reading(token => PumpAsync(nookId, process, offset, token), ct);
        while (true)
        {
            if (progress.Idle)
            {
                bool retired = retire(this);
                if (retired)
                {
                    return true;
                }
            }

            List<RunnerInput> batch = await NextBatchAsync(ct);
            bool exited = await HandleAsync(batch, ct);
            if (exited)
            {
                return false;
            }

            progress = await AdvanceAsync(ct);
            bool replaced = progress.Harness != process;
            if (replaced)
            {
                return false;
            }
        }
    }

    private async Task<List<RunnerInput>> NextBatchAsync(CancellationToken ct)
    {
        RunnerInput first = await _inputs.Reader.ReadAsync(ct);
        List<RunnerInput> batch = [first];
        while (batch.Count < MaxBatch && _inputs.Reader.TryRead(out RunnerInput more))
        {
            batch.Add(more);
        }

        return batch;
    }

    // Does whatever the chat's state calls for: stop, announce new messages, start the agent, start a
    // turn, steer messages into the running one.
    private async Task<Progress> AdvanceAsync(CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);
        Chat chat = await db.Chats.SingleAsync(found => found.Id == chatId, ct);
        List<Message> waiting = await db.Messages
            .Where(message => message.ChatId == chatId && (message.State == MessageState.New || message.State == MessageState.Queued || message.State == MessageState.Steering))
            .OrderBy(message => message.Id)
            .ToListAsync(ct);
        List<string> outgoing = [];

        bool stopRequested = Interlocked.Exchange(ref _stopRequested, 0) == 1;
        if (stopRequested)
        {
            StopTurn(db, chat, waiting, outgoing);
        }

        foreach (Message message in waiting.Where(message => message.State == MessageState.New))
        {
            db.Events.Add(chat.Record(new ChatEventBody(new MessageSent(message.Id, message.SentBy, message.Text)), time));
            message.Queue();
        }

        List<Message> queued = waiting.Where(message => message.State == MessageState.Queued).ToList();
        if (chat.HarnessProcessId is null && queued.Count > 0)
        {
            await StartHarnessAsync(db, chat, queued[0], outgoing, ct);
        }
        else if (chat.SessionId is not null)
        {
            Deliver(db, chat, chat.SessionId, queued, outgoing);
        }

        await SaveAsync(db, ct);
        await SendAsync(chat, outgoing, ct);
        bool idle = chat.TurnMessageId is null && !waiting.Any(message => message.Waiting)
            && (chat.HarnessProcessId is null || chat.SessionId is not null);
        return new Progress(chat.NookId, chat.HarnessProcessId, chat.OutputOffset, idle);
    }

    // The turn ends now; whatever the agent still sends about it is kept, but no longer waited for.
    // Messages it hasn't received are cancelled.
    private void StopTurn(ChatsDbContext db, Chat chat, List<Message> waiting, List<string> outgoing)
    {
        if (chat.TurnMessageId is not null)
        {
            db.Events.Add(chat.Record(new ChatEventBody(new TurnEnded("cancelled", Failure: null)), time));
            chat.TurnEnded();
            _agentFinishedTurn = false;
            if (chat.SessionId is not null)
            {
                outgoing.Add(Acp.Cancel(chat.SessionId));
            }
        }

        foreach (Message message in waiting.Where(message => message.State is MessageState.New or MessageState.Queued))
        {
            if (message.State == MessageState.New)
            {
                db.Events.Add(chat.Record(new ChatEventBody(new MessageSent(message.Id, message.SentBy, message.Text)), time));
            }

            db.Events.Add(chat.Record(new ChatEventBody(new MessageCancelled(message.Id)), time));
            message.Cancel();
        }
    }

    // With no turn running, the oldest queued message starts one; the rest join the running turn when
    // the agent supports steering, and otherwise wait for the next.
    private void Deliver(ChatsDbContext db, Chat chat, string sessionId, List<Message> queued, List<string> outgoing)
    {
        if (chat.TurnMessageId is null && queued.Count > 0)
        {
            Message first = queued[0];
            queued.RemoveAt(0);
            db.Events.Add(chat.Record(new ChatEventBody(new TurnStarted(first.Id)), time));
            first.Deliver();
            chat.TurnStarted(first.Id);
            outgoing.Add(Acp.Prompt(first.Id.Value, sessionId, first.Text));
        }

        if (chat.TurnMessageId is not null && chat.SupportsSteering && !_agentFinishedTurn)
        {
            foreach (Message message in queued)
            {
                message.Steer();
                outgoing.Add(Acp.Steer(message.Id.Value, sessionId, message.Text));
            }
        }
    }

    private async Task StartHarnessAsync(ChatsDbContext db, Chat chat, Message first, List<string> outgoing, CancellationToken ct)
    {
        // Each waiting message tries once, so an agent that can't start ends with every message answered.
        HarnessProfile? harness = HarnessProfiles.Find(chat.Harness);
        if (harness is null)
        {
            Fail(db, chat, first, "The agent couldn't start: its harness is no longer offered.");
            return;
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        Result<AgentAccountCredential> used = await scope.ServiceProvider.GetRequiredService<IAgentAccountsApi>()
            .UseAsync(SystemActors.Harness, chat.AgentAccountId, chat.WorkspaceId, ct);
        if (used.Failed)
        {
            Fail(db, chat, first, "The agent couldn't start: " + used.Error.Message);
            return;
        }

        AgentAccountCredential account = used.Output;

        // A secret that stays behind the model gateway is replaced by a token for this chat, saved
        // first so the agent's first model call finds it.
        CredentialKind kind = AccountCredentials.KindOf(account.Kind);
        string credential = harness.UsesGateway(kind) ? chat.IssueHarnessToken() : account.Secret;
        await SaveAsync(db, ct);

        IReadOnlyDictionary<string, string> environment = harness.EnvironmentFor(kind, credential, settings.ModelGatewayUrl);
        Result<ProcessId> started = await agent.StartAsync(chat.NookId, harness, environment, ct);
        if (started.Failed)
        {
            Fail(db, chat, first, "The agent couldn't start: " + started.Error.Message);
            return;
        }

        chat.HarnessStarted(started.Output);
        outgoing.Add(Acp.Initialize());
    }

    // Returns true when the agent's process ended.
    private async Task<bool> HandleAsync(List<RunnerInput> batch, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);
        Chat chat = await db.Chats.SingleAsync(found => found.Id == chatId, ct);
        List<string> outgoing = [];
        bool exited = false;
        foreach (RunnerInput input in batch)
        {
            switch (input)
            {
                case WakeUp:
                    break;
                case HarnessLine line:
                    await HandleLineAsync(db, chat, line.Text, outgoing, ct);
                    chat.Read(line.EndOffset);
                    break;
                case HarnessExited ended:
                    await HandleExitAsync(db, chat, ended.ExitCode, ct);
                    exited = true;
                    break;
                case ReaderFailed failed:
                    throw new InvalidOperationException("Reading the agent's output of chat " + chatId.Value + " failed.", failed.Exception);
            }
        }

        await SaveAsync(db, ct);
        await SendAsync(chat, outgoing, ct);
        return exited;
    }

    private async Task HandleLineAsync(ChatsDbContext db, Chat chat, string line, List<string> outgoing, CancellationToken ct)
    {
        AcpEvent? read = Acp.Read(line);
        if (read is null)
        {
            return;
        }

        switch (read.Value)
        {
            case AcpUpdate update:
                db.Events.Add(chat.Record(new ChatEventBody(new AgentUpdate(update.Update)), time));
                break;
            case AcpRequest request:
                outgoing.Add(request.IsPermissionRequest ? Acp.Allow(request) : Acp.MethodNotFound(request));
                break;
            case AcpInitialized initialized:
                chat.Initialized(initialized.SupportsSteering);
                outgoing.Add(Acp.NewSession(AgentProcess.WorkingDirectory));
                break;
            case AcpSessionCreated created:
                chat.SessionReady(created.SessionId);
                break;
            case AcpStartFailed failed:
                await FailStartAsync(db, chat, failed.Error, ct);
                break;
            case AcpPromptEnded ended:
                EndTurn(db, chat, MessageId.From(ended.Prompt), new TurnEnded(ended.StopReason, Failure: null));
                break;
            case AcpPromptFailed failed:
                EndTurn(db, chat, MessageId.From(failed.Prompt), new TurnEnded("failed", failed.Error));
                break;
            case AcpSteerAnswered answered:
                await HandleSteerAnswerAsync(db, chat, MessageId.From(answered.Message), answered.Injected, ct);
                break;
        }
    }

    // The agent can't be used: the first waiting message gets the answer, and the process goes.
    private async Task FailStartAsync(ChatsDbContext db, Chat chat, string error, CancellationToken ct)
    {
        Message? first = await db.Messages.Where(message => message.ChatId == chatId && message.State == MessageState.Queued).OrderBy(message => message.Id).FirstOrDefaultAsync(ct);
        if (first is not null)
        {
            Fail(db, chat, first, "The agent couldn't start: " + error);
        }

        await StopHarnessAsync(chat, ct);
    }

    // A stopped turn already ended; its late answer changes nothing.
    private void EndTurn(ChatsDbContext db, Chat chat, MessageId prompt, TurnEnded ended)
    {
        if (chat.TurnMessageId != prompt)
        {
            return;
        }

        db.Events.Add(chat.Record(new ChatEventBody(ended), time));
        chat.TurnEnded();
        _agentFinishedTurn = false;
    }

    private async Task HandleSteerAnswerAsync(ChatsDbContext db, Chat chat, MessageId steered, bool injected, CancellationToken ct)
    {
        Message? message = await db.Messages.SingleOrDefaultAsync(found => found.Id == steered && found.State == MessageState.Steering, ct);
        if (message is null)
        {
            return;
        }

        if (injected)
        {
            message.Deliver();
            db.Events.Add(chat.Record(new ChatEventBody(new MessageSteered(message.Id)), time));
            return;
        }

        // No running turn to join: it starts the next one.
        message.Queue();
        _agentFinishedTurn = true;
    }

    private async Task HandleExitAsync(ChatsDbContext db, Chat chat, int exitCode, CancellationToken ct)
    {
        if (chat.TurnMessageId is not null)
        {
            string failure = string.Create(CultureInfo.InvariantCulture, $"The agent stopped with exit code {exitCode}.");
            db.Events.Add(chat.Record(new ChatEventBody(new TurnEnded("failed", failure)), time));
        }

        List<Message> steering = await db.Messages.Where(message => message.ChatId == chatId && message.State == MessageState.Steering).ToListAsync(ct);
        foreach (Message message in steering)
        {
            message.Queue();
        }

        // Not handled: resuming the conversation in the next agent (session/load); the next message
        // starts a new session.
        chat.HarnessStopped();
        _agentFinishedTurn = false;
    }

    private void Fail(ChatsDbContext db, Chat chat, Message message, string failure)
    {
        db.Events.Add(chat.Record(new ChatEventBody(new TurnStarted(message.Id)), time));
        db.Events.Add(chat.Record(new ChatEventBody(new TurnEnded("failed", failure)), time));
        message.Deliver();
        Log.AgentFailed(logger, chatId.Value, failure);
    }

    // Hands each line of the agent's output, and its exit, to the runner.
    private async Task PumpAsync(NookId nookId, ProcessId processId, long offset, CancellationToken ct)
    {
        try
        {
            await foreach (AgentOutput output in agent.ReadAsync(nookId, processId, offset, ct))
            {
                RunnerInput input = output switch
                {
                    HarnessLine line => new RunnerInput(line),
                    HarnessExited exited => new RunnerInput(exited),
                };
                await _inputs.Writer.WriteAsync(input, ct);
            }
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            await _inputs.Writer.WriteAsync(new RunnerInput(new ReaderFailed(exception)), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The runner stopped reading.
        }
    }

    private async Task SendAsync(Chat chat, List<string> lines, CancellationToken ct)
    {
        if (lines.Count == 0 || chat.HarnessProcessId is null)
        {
            return;
        }

        Result sent = await agent.SendAsync(chat.NookId, chat.HarnessProcessId.Value, lines, ct);
        if (sent.Failed)
        {
            // The process is gone or out of reach; its exit arrives through the reader.
            Log.InputFailed(logger, chatId.Value, sent.Error.Message);
        }
    }

    private async Task StopHarnessAsync(Chat chat, CancellationToken ct)
    {
        if (chat.HarnessProcessId is null)
        {
            return;
        }

        // Not handled: a nook out of reach keeps the unusable agent running until its next exit; the
        // chat has already answered the message that waited for it.
        _ = await agent.StopAsync(chat.NookId, chat.HarnessProcessId.Value, ct);
    }

    private async Task SaveAsync(ChatsDbContext db, CancellationToken ct)
    {
        bool recorded = db.ChangeTracker.Entries<StoredEvent>().Any(entry => entry.State == EntityState.Added);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            // The runner is the chat's only writer, so a conflict is a defect.
            throw new InvalidOperationException("Saving chat " + chatId.Value + " failed: " + saved.Error.Message);
        }

        if (recorded)
        {
            signals.Notify(chatId);
        }
    }

    private sealed record Progress(NookId NookId, ProcessId? Harness, long OutputOffset, bool Idle);

    // The reader of the agent's output; disposing it stops the reader and waits for it.
    private sealed class Reading : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly Task _done;

        public Reading(Func<CancellationToken, Task> read, CancellationToken ct)
        {
            _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _done = read(_stop.Token);
        }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _done;
            _stop.Dispose();
        }
    }
}
