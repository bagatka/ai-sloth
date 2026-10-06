using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
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
    HarnessStates states,
    AgentInstructions instructions,
    NookSetups setups,
    ChatsMeter meter,
    ChatsSettings settings,
    ChatSignals signals,
    TimeProvider time,
    ILogger logger)
{
    private const int MaxBatch = 200;

    // How agents' model calls name this client, for their providers' attribution.
    private const string ClientName = "aisloth";
    private const string ClientTitle = "AiSloth";

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    // A chat with work keeps its nook awake this long, renewing it this often, whatever the nook's own
    // sleep period: its agent may think for minutes without a word.
    private static readonly TimeSpan KeepAwakeFor = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan KeepAwakeEvery = TimeSpan.FromSeconds(10);

    private readonly Channel<RunnerInput> _inputs = Channel.CreateBounded<RunnerInput>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    // The agent answered a steer with promptRequired: its turn is over even if its answer to the prompt
    // isn't read yet, so nothing more is offered to it until the turn ends here too.
    private bool _agentFinishedTurn;
    private int _stopRequested;

    // When the message of the running turn was sent, until its agent first did something for it.
    private DateTimeOffset? _firstActionDue;

    // When this runner last kept the chat's nook awake.
    private DateTimeOffset _keptAwakeAt = DateTimeOffset.MinValue;

    public ChatId ChatId => chatId;

    private static string ClientVersion => typeof(ChatRunner).Assembly.GetName().Version?.ToString() ?? "0";

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
        // What the chat waits for in the background is done only while the runner runs, and so is the
        // tick that has it keep its nook awake while it has work.
        await using Waits waits = new Waits(Wake, KeepAwakeEvery, time, ct);
        while (true)
        {
            bool retired = await RunOnceAsync(retire, waits, ct);
            if (retired)
            {
                return;
            }
        }
    }

    // Works until the agent's process ends, or the runner retires; returns whether it retired.
    private async Task<bool> RunOnceAsync(Func<ChatRunner, bool> retire, Waits waits, CancellationToken ct)
    {
        try
        {
            Progress progress = await AdvanceAsync(waits, ct);
            if (progress.Harness is not null)
            {
                return await ServeAsync(progress, progress.Harness.Value, retire, waits, ct);
            }

            if (progress.Idle && retire(this))
            {
                return true;
            }

            List<RunnerInput> batch = await NextBatchAsync(ct);
            await HandleAsync(batch, ct);
            return false;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            Log.RunnerFailed(logger, exception, chatId.Value);

            // Lines already queued were read past the saved offset; the next reader reads them again.
            while (_inputs.Reader.TryRead(out _))
            {
            }

            await Task.Delay(RetryDelay, time, ct);
            return false;
        }
    }

    // Works with one agent process, reading its output, until the process ends or is replaced, or the
    // chat is idle and the runner retires; returns whether it retired.
    private async Task<bool> ServeAsync(Progress progress, ProcessId process, Func<ChatRunner, bool> retire, Waits waits, CancellationToken ct)
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

            progress = await AdvanceAsync(waits, ct);
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
    private async Task<Progress> AdvanceAsync(Waits waits, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);
        Chat chat = await db.Chats.SingleAsync(found => found.Id == chatId, ct);
        List<Message> waiting = await db.Messages
            .Where(message => message.ChatId == chatId && (message.State == MessageState.New || message.State == MessageState.Queued || message.State == MessageState.Steering))
            .OrderBy(message => message.Id)
            .ToListAsync(ct);
        List<string> outgoing = [];

        foreach (Message message in waiting.Where(message => message.State == MessageState.New))
        {
            Announce(db, chat, message);
        }

        bool stopRequested = Interlocked.Exchange(ref _stopRequested, 0) == 1;
        if (stopRequested)
        {
            StopTurn(db, chat, waiting, outgoing);
            await StopSetupTestAsync(db, chat, waits, ct);
        }

        // The files a turn changed are saved before the next turn changes them again.
        if (chat.CheckpointAfter is MessageId after && chat.TurnMessageId is null)
        {
            await SaveAsync(db, ct);
            await TakeCheckpointAsync(db, chat, after, ct);
        }

        // A setup the agent prepared is tested from its turn's checkpoint, before the next turn.
        if (chat.SetupTestAfter is MessageId preparing && chat.TurnMessageId is null && chat.CheckpointAfter is null)
        {
            await TestSetupAsync(db, chat, preparing, waits, ct);
        }

        List<Message> queued = waiting.Where(message => message.State == MessageState.Queued).ToList();
        if (chat.HarnessProcessId is null && (queued.Count > 0 || chat.TurnMessageId is not null || chat.StartsAgent))
        {
            bool setUp = await SetUpAsync(db, chat, queued.FirstOrDefault(), waits, ct);
            if (setUp)
            {
                await StartHarnessAsync(db, chat, queued.FirstOrDefault(), outgoing, ct);
            }
        }
        else if (chat.SessionId is not null && chat.SetupTestAfter is null)
        {
            await CatchUpStateAsync(chat, queued, ct);
            Deliver(db, chat, chat.SessionId, queued, outgoing);
        }

        await SaveAsync(db, ct);
        await SendAsync(chat, outgoing, ct);
        bool idle = chat.TurnMessageId is null && !waiting.Any(message => message.Waiting) && !chat.StartsAgent
            && chat.SetupTestAfter is null && (chat.HarnessProcessId is null || chat.SessionId is not null);
        if (!idle)
        {
            await KeepAwakeAsync(chat, ct);
        }

        return new Progress(chat.NookId, chat.HarnessProcessId, chat.OutputOffset, idle);
    }

    // A chat with work keeps its nook from falling asleep, and wakes it when it sleeps. A nook that can't
    // be woken now is woken by the next operation, which the chat's work makes anyway.
    private async Task KeepAwakeAsync(Chat chat, CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (now - _keptAwakeAt < KeepAwakeEvery)
        {
            return;
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        Result kept = await scope.ServiceProvider.GetRequiredService<INooksApi>().WakeAsync(SystemActors.Harness, new WakeNook(chat.NookId, KeepAwakeFor), ct);
        if (!kept.Failed)
        {
            _keptAwakeAt = now;
        }
    }

    // Everyone in the chat sees a new message: one for the agent queues for it, and a proposal stays
    // with the people in the chat.
    private void Announce(ChatsDbContext db, Chat chat, Message message)
    {
        if (message.IsProposal)
        {
            db.Events.Add(chat.Record(new ChatEventBody(new MessageProposed(message.Id, message.SentBy, message.Text)), time));
            message.Propose();
            return;
        }

        db.Events.Add(chat.Record(new ChatEventBody(new MessageSent(message.Id, message.SentBy, message.Text, message.ProposalId)), time));
        message.Queue();
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

        foreach (Message message in waiting.Where(message => message.State == MessageState.Queued))
        {
            db.Events.Add(chat.Record(new ChatEventBody(new MessageCancelled(message.Id)), time));
            message.Cancel();
        }
    }

    // Takes the checkpoint after the message's turn, and syncs the harness state. A failed
    // checkpoint is told and the chat goes on: the next turn's checkpoint keeps its files too.
    private async Task TakeCheckpointAsync(ChatsDbContext db, Chat chat, MessageId after, CancellationToken ct)
    {
        string text = await db.Messages.Where(message => message.Id == after).Select(message => message.Text).SingleAsync(ct);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        Result<CheckpointSummary> taken = await scope.ServiceProvider.GetRequiredService<INooksApi>()
            .CheckpointAsync(SystemActors.Harness, new CheckpointNook(chat.NookId, Note(text)), ct);
        ChatEventBody told = taken.Failed
            ? new ChatEventBody(new CheckpointFailed(taken.Error.Message))
            : new ChatEventBody(new CheckpointSaved(taken.Output.Number));
        db.Events.Add(chat.Record(told, time));
        chat.CheckpointTaken();
        string? state = await states.SyncAsync(chat, ct);
        chat.HarnessStateSynced(state);
    }

    // Before a turn starts, its agent's nook gets the harness state its person's other chats saved since.
    private async Task CatchUpStateAsync(Chat chat, List<Message> queued, CancellationToken ct)
    {
        if (chat.TurnMessageId is not null || queued.Count == 0)
        {
            return;
        }

        bool moved = await states.MovedAsync(chat, ct);
        if (moved)
        {
            string? state = await states.SyncAsync(chat, ct);
            chat.HarnessStateSynced(state);
        }
    }

    // A checkpoint's note: the first line of the message, cut to fit.
    private static string Note(string text)
    {
        const int MaxLength = 200;
        string line = text.Trim().Split('\n', 2)[0].Trim();
        return line.Length <= MaxLength ? line : line[..(MaxLength - 1)] + "…";
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
            chat.TurnStarted(first.Id, testsSetup: first.SetupTest is not null);
            _firstActionDue = first.SentAt;
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

    // Starts an agent with the chat, for the turn a lost one was working on, or for the first queued
    // message, once the nook's setup ended.
    private async Task StartHarnessAsync(ChatsDbContext db, Chat chat, Message? first, List<string> outgoing, CancellationToken ct)
    {
        // Each waiting message tries once, so an agent that can't start ends with every message answered.
        chat.AgentStartTried();
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

        // A model API stays behind the model gateway: the agent gets a token for this chat instead,
        // saved first so its first model call finds it. A plan's token tied to the harness goes to it.
        CredentialKind kind = AccountCredentials.KindOf(account.Kind);
        IReadOnlyDictionary<string, string> environment;
        if (account.Access is HarnessToken token)
        {
            environment = HarnessProfile.EnvironmentFor(kind, token.Value, gateway: null);
        }
        else
        {
            environment = HarnessProfile.EnvironmentFor(kind, chat.IssueHarnessToken(), settings.ModelGatewayUrl);
        }

        await SaveAsync(db, ct);

        // The agent starts with its instructions and its starter's harness state, after the nook's files
        // are in place. Instructions are rules people rely on, so it never starts without them.
        Result instructed = await instructions.WriteAsync(chat, harness, ct);
        if (instructed.Failed)
        {
            Fail(db, chat, first, "The agent couldn't start: writing its instructions failed: " + instructed.Error.Message);
            return;
        }

        string? state = await states.SyncAsync(chat, ct);
        chat.HarnessStateSynced(state);
        Result<ProcessId> started = await agent.StartAsync(chat.NookId, environment, ct);
        if (started.Failed)
        {
            Fail(db, chat, first, "The agent couldn't start: " + started.Error.Message);
            return;
        }

        chat.HarnessStarted(started.Output);
        outgoing.Add(Acp.Initialize(ClientName, ClientTitle, ClientVersion));
    }

    // The agent starts once its nook's setup ended. Putting the nook's files in place starts it, and the
    // chat tells when it started and how it ended. Waiting holds up nothing else: a watch wakes the
    // runner when the run ends. Returns whether the agent may start; a nook whose setup can't be had
    // fails the start.
    private async Task<bool> SetUpAsync(ChatsDbContext db, Chat chat, Message? first, Waits waits, CancellationToken ct)
    {
        Result<NookSetup> setup;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            setup = await scope.ServiceProvider.GetRequiredService<INooksApi>().GetSetupAsync(SystemActors.Harness, chat.NookId, ct);
        }

        if (setup.Failed)
        {
            Fail(db, chat, first, "The agent couldn't start: " + setup.Error.Message);
            chat.AgentStartTried();
            return false;
        }

        if (setup.Output.Run is not SetupRun run)
        {
            return true;
        }

        bool news = chat.SetupRunSeen(run.Process);
        if (news)
        {
            db.Events.Add(chat.Record(new ChatEventBody(new SetupStarted(setup.Output.Scripts, setup.Output.ReadyCopyMadeAt is not null)), time));
        }

        if (!chat.WaitsForSetup)
        {
            return true;
        }

        SetupEnd? ended = await waits.SetupRun.ResultAsync(run.Process, token => ThenWakeAsync(setups.FollowAsync(chat.NookId, run.Process, token)), ct);
        if (ended is null)
        {
            return false;
        }

        chat.SetupRunEnded(ended.ExitCode);
        TimeSpan took = (run.EndedAt ?? time.GetUtcNow()) - run.StartedAt;
        string? output = ended.ExitCode == 0 ? null : ended.Output;
        db.Events.Add(chat.Record(new ChatEventBody(new SetupEnded(ended.ExitCode, took, output)), time));
        return true;
    }

    // Background work wakes the runner when it ends, so the chat moves on.
    private async Task<T> ThenWakeAsync<T>(Task<T> work)
    {
        try
        {
            return await work;
        }
        finally
        {
            Wake();
        }
    }

    // Tests the setup the agent prepared in a fresh nook, in the background, as the person who asked:
    // the chat tells when the test starts and how it ended, and a failure goes back to the agent to fix,
    // until the last test.
    private async Task TestSetupAsync(ChatsDbContext db, Chat chat, MessageId preparing, Waits waits, CancellationToken ct)
    {
        Message asked = await db.Messages.SingleAsync(message => message.Id == preparing, ct);
        int test = asked.SetupTest ?? 1;
        if (!waits.SetupTest.Works(preparing))
        {
            db.Events.Add(chat.Record(new ChatEventBody(new SetupTestStarted(test)), time));
        }

        Actor person = Actor.ForUser(asked.SentBy);
        SetupTestResult? result = await waits.SetupTest.ResultAsync(preparing, token => ThenWakeAsync(setups.TestAsync(person, chat.NookId, chat.Harness, token)), ct);
        if (result is null)
        {
            return;
        }

        bool fixes = result.ExitCode > 0 && test < NookSetups.MaxTests;
        db.Events.Add(chat.Record(new ChatEventBody(new SetupTested(test, result.ExitCode, result.FromScratch, result.Again, result.Output, fixes)), time));
        chat.SetupTestEnded();
        if (!fixes)
        {
            return;
        }

        Result<Message> fix = Message.Send(chat.Id, asked.SentBy, NookSetups.FixRequest(result.Output ?? string.Empty), isProposal: false, proposalId: null, test + 1, time);
        if (fix.Failed)
        {
            throw new InvalidOperationException("Asking chat " + chatId.Value + "'s agent to fix its setup failed: " + fix.Error.Message);
        }

        db.Messages.Add(fix.Output);
        Wake();
    }

    // Stopping the agent stops testing its setup too, whether the test was due or running.
    private async Task StopSetupTestAsync(ChatsDbContext db, Chat chat, Waits waits, CancellationToken ct)
    {
        if (chat.SetupTestAfter is not MessageId preparing)
        {
            return;
        }

        bool running = waits.SetupTest.Works(preparing);
        await waits.SetupTest.StopAsync();
        chat.SetupTestEnded();
        if (running)
        {
            int test = await db.Messages.Where(message => message.Id == preparing).Select(message => message.SetupTest ?? 1).SingleAsync(ct);
            db.Events.Add(chat.Record(new ChatEventBody(new SetupTested(test, ProcessExited.Lost, TimeSpan.Zero, Again: null, "Stopped.", AgentFixes: false)), time));
        }
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
            case AcpUpdate update when !chat.LoadingSession:
                db.Events.Add(chat.Record(new ChatEventBody(new AgentUpdate(update.Update)), time));
                MeasureFirstAction(chat, update);
                break;
            case AcpUpdate:
                // The loaded session's history, which the chat recorded the first time.
                break;
            case AcpRequest request:
                outgoing.Add(request.IsPermissionRequest ? Acp.Allow(request) : Acp.MethodNotFound(request));
                break;
            case AcpInitialized initialized:
                string? earlier = chat.Initialized(initialized.SupportsSteering, initialized.SupportsLoading);
                outgoing.Add(earlier is null ? Acp.NewSession(AgentProcess.WorkingDirectory) : Acp.LoadSession(earlier, AgentProcess.WorkingDirectory));
                break;
            case AcpSessionLoaded:
                chat.SessionLoaded();
                db.Events.Add(chat.Record(new ChatEventBody(new AgentRestarted(Remembers: true)), time));
                await CarryOverTurnAsync(db, chat, outgoing, ct);
                break;
            case AcpLoadFailed:
                chat.LoadFailed();
                outgoing.Add(Acp.NewSession(AgentProcess.WorkingDirectory));
                break;
            case AcpSessionCreated created:
                bool tookOver = chat.SessionReady(created.SessionId);
                if (tookOver)
                {
                    db.Events.Add(chat.Record(new ChatEventBody(new AgentRestarted(Remembers: false)), time));
                }

                await CarryOverTurnAsync(db, chat, outgoing, ct);
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

    // The first time the agent thinks, answers, or uses a tool in a turn, how long its message waited.
    private void MeasureFirstAction(Chat chat, AcpUpdate update)
    {
        bool named = update.Update.TryGetProperty("sessionUpdate", out JsonElement kind);
        bool acted = named && kind.GetString() is "agent_thought_chunk" or "agent_message_chunk" or "tool_call" or "plan";
        if (acted && chat.TurnMessageId is not null && _firstActionDue is DateTimeOffset sentAt)
        {
            meter.FirstAction(chat.Harness, time.GetUtcNow() - sentAt);
            _firstActionDue = null;
        }
    }

    // The agent can't be used: the turn a lost one was working on, or the first waiting message,
    // gets the answer, and the process goes.
    private async Task FailStartAsync(ChatsDbContext db, Chat chat, string error, CancellationToken ct)
    {
        Message? first = await db.Messages.Where(message => message.ChatId == chatId && message.State == MessageState.Queued).OrderBy(message => message.Id).FirstOrDefaultAsync(ct);
        Fail(db, chat, first, "The agent couldn't start: " + error);
        await StopHarnessAsync(chat, ct);
    }

    // A new agent's session is ready while a turn runs: the turn a lost agent was working on, whose
    // message it gets again.
    private static async Task CarryOverTurnAsync(ChatsDbContext db, Chat chat, List<string> outgoing, CancellationToken ct)
    {
        if (chat.TurnMessageId is not MessageId turn || chat.SessionId is null)
        {
            return;
        }

        string text = await db.Messages.Where(message => message.Id == turn).Select(message => message.Text).SingleAsync(ct);
        outgoing.Add(Acp.Prompt(turn.Value, chat.SessionId, text));
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

    // An agent lost with its nook hands its turn to the next agent, which works on the files of the
    // nook's latest checkpoint; any other exit ends the turn as failed.
    private async Task HandleExitAsync(ChatsDbContext db, Chat chat, int exitCode, CancellationToken ct)
    {
        bool lost = exitCode == ProcessExited.Lost;
        if (chat.TurnMessageId is not null && !lost)
        {
            string failure = string.Create(CultureInfo.InvariantCulture, $"The agent stopped with exit code {exitCode}.");
            db.Events.Add(chat.Record(new ChatEventBody(new TurnEnded("failed", failure)), time));
            chat.TurnEnded();
        }

        List<Message> steering = await db.Messages.Where(message => message.ChatId == chatId && message.State == MessageState.Steering).ToListAsync(ct);
        foreach (Message message in steering)
        {
            message.Queue();
        }

        // The next agent loads this one's session when it can.
        if (lost)
        {
            chat.HarnessLost();
        }
        else
        {
            chat.HarnessStopped();
        }

        _agentFinishedTurn = false;
    }

    // The turn a lost agent was working on ends as failed; otherwise the message gets a turn that
    // fails at once.
    private void Fail(ChatsDbContext db, Chat chat, Message? message, string failure)
    {
        if (chat.TurnMessageId is not null)
        {
            db.Events.Add(chat.Record(new ChatEventBody(new TurnEnded("failed", failure)), time));
            chat.TurnEnded();
        }
        else if (message is not null)
        {
            db.Events.Add(chat.Record(new ChatEventBody(new TurnStarted(message.Id)), time));
            db.Events.Add(chat.Record(new ChatEventBody(new TurnEnded("failed", failure)), time));
            message.Deliver();
        }

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

    // What a runner waits for in the background, stopped when it stops: following the setup run its
    // agent waits for, testing a setup the agent prepared in a fresh nook, and a regular tick that has
    // the runner look at its chat again.
    private sealed class Waits : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop;
        private readonly Task _ticking;

        public Waits(Action tick, TimeSpan every, TimeProvider time, CancellationToken ct)
        {
            _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ticking = TickAsync(tick, every, time, _stop.Token);
        }

        public Background<SetupEnd> SetupRun { get; } = new Background<SetupEnd>();

        public Background<SetupTestResult> SetupTest { get; } = new Background<SetupTestResult>();

        public async ValueTask DisposeAsync()
        {
            await SetupRun.StopAsync();
            await SetupTest.StopAsync();
            await _stop.CancelAsync();
            await _ticking;
            _stop.Dispose();
        }

        private static async Task TickAsync(Action tick, TimeSpan every, TimeProvider time, CancellationToken ct)
        {
            using PeriodicTimer timer = new PeriodicTimer(every, time);
            try
            {
                while (await timer.WaitForNextTickAsync(ct))
                {
                    tick();
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // The runner stopped.
            }
        }
    }

    // One piece of work at a time in the background, for what it's about (its key), until it ends;
    // stopping it cancels it and waits for it.
    private sealed class Background<T>
        where T : class
    {
        private object? _key;
        private CancellationTokenSource? _stop;
        private Task<T>? _done;

        // Whether it works, or worked, on this.
        public bool Works(object key)
        {
            return _done is not null && Equals(_key, key);
        }

        // Its result once it ended; until then null, working on it from now on, instead of anything else.
        public async Task<T?> ResultAsync(object key, Func<CancellationToken, Task<T>> work, CancellationToken ct)
        {
            if (_done is null || !Equals(_key, key))
            {
                await StopAsync();
                _key = key;
                _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
                _done = work(_stop.Token);
            }

            if (!_done.IsCompletedSuccessfully)
            {
                return null;
            }

            return await _done;
        }

        public async Task StopAsync()
        {
            if (_stop is null || _done is null)
            {
                return;
            }

            await _stop.CancelAsync();
            try
            {
                await _done;
            }
            catch (OperationCanceledException)
            {
                // Stopped, as asked.
            }

            _stop.Dispose();
            _stop = null;
            _done = null;
            _key = null;
        }
    }

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
