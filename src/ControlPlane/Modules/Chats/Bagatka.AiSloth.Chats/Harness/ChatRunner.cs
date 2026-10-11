using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Harnesses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Bagatka.ModelAccess;

namespace Bagatka.AiSloth.Chats.Harness;

// Talks to one chat's agent over ACP, through its process's lines: starts it, hands it messages,
// answers its requests, and saves what it reports. ChatRunners keeps at most one runner per chat. A
// runner exits when its chat is idle; how far the agent's output was read is saved with every change,
// so the next runner, even after a restart, continues exactly there.
internal sealed class ChatRunner(
    ChatId chatId,
    IDbContextFactory<ChatsDbContext> databases,
    INooksApi nooks,
    IAgentAccountsApi accounts,
    AgentProcess agent,
    HarnessStates states,
    AgentInstructions instructions,
    NookSetups setups,
    ChatsMeter meter,
    ChatsSettings settings,
    ChatSignals signals,
    IProductEvents productEvents,
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

    // The agent stopped for another account or model chosen meanwhile, or for its nook's setup to run
    // again, until its exit arrives.
    private ProcessId? _stoppedForChange;

    // The agent couldn't start and was stopped, its waiting message answered, until its exit arrives.
    private bool _startFailed;

    // What the chat's turns tell the people who run AiSloth: traces, timings, and product events.
    private readonly TurnTelemetry _telemetry = new TurnTelemetry(chatId, meter, productEvents, time);

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
        await using RunnerWaits waits = new RunnerWaits(Wake, KeepAwakeEvery, time, ct);
        try
        {
            while (true)
            {
                bool retired = await RunOnceAsync(retire, waits, ct);
                if (retired)
                {
                    return;
                }
            }
        }
        finally
        {
            _telemetry.Stopped();
        }
    }

    // Works until the agent's process ends, or the runner retires; returns whether it retired.
    private async Task<bool> RunOnceAsync(Func<ChatRunner, bool> retire, RunnerWaits waits, CancellationToken ct)
    {
        try
        {
            Progress? advanced = await AdvanceAsync(waits, ct);

            // A draft that went with its nook leaves its runner nothing to do.
            if (advanced is not Progress progress)
            {
                while (_inputs.Reader.TryRead(out _))
                {
                }

                Interlocked.Exchange(ref _stopRequested, 0);
                return retire(this);
            }

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
        catch (DbUpdateConcurrencyException) when (!ct.IsCancellationRequested)
        {
            // The chat changed meanwhile: the runner reads it again at once. Lines already queued were
            // read past the saved offset; the next reader reads them again.
            while (_inputs.Reader.TryRead(out _))
            {
            }

            return false;
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            await FailedAsync(exception, ct);
            return false;
        }
    }

    // What failed is logged, and the chat says it's stuck, once, so nobody waits on it without knowing;
    // when saying so fails too, the next failure says it. A draft that went with its nook fails what
    // its runner was doing; the next try retires it. Then the runner waits a moment to try again.
    private async Task FailedAsync(Exception exception, CancellationToken ct)
    {
        await using (ChatsDbContext db = await databases.CreateDbContextAsync(ct))
        {
            Chat? chat = await db.Chats.SingleOrDefaultAsync(found => found.Id == chatId, ct);
            StoredEvent? failed = chat?.WorkFailed(time);
            if (chat is not null)
            {
                Log.RunnerFailed(logger, exception, chatId.Value);
            }

            if (failed is not null)
            {
                db.Events.Add(failed);
                Result saved = await db.SaveAsync(ct);
                if (!saved.Failed)
                {
                    signals.Notify(chatId);
                }
            }
        }

        // Lines already queued were read past the saved offset; the next reader reads them again.
        while (_inputs.Reader.TryRead(out _))
        {
        }

        await Task.Delay(RetryDelay, time, ct);
    }

    // Works with one agent process, reading its output, until the process ends or is replaced, or the
    // chat is idle and the runner retires; returns whether it retired.
    private async Task<bool> ServeAsync(Progress progress, ProcessId process, Func<ChatRunner, bool> retire, RunnerWaits waits, CancellationToken ct)
    {
        NookId nookId = progress.NookId;
        long offset = progress.OutputOffset;
        await using OutputReading reading = new OutputReading(token => PumpAsync(nookId, process, offset, token), ct);
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

            Progress? advanced = await AdvanceAsync(waits, ct);
            if (advanced is null || advanced.Harness != process)
            {
                return false;
            }

            progress = advanced;
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
    // turn, steer messages into the running one. Null when the chat is gone.
    private async Task<Progress?> AdvanceAsync(RunnerWaits waits, CancellationToken ct)
    {
        Activity.Current = _telemetry.Turn;
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);
        Chat? chat = await db.Chats.SingleOrDefaultAsync(found => found.Id == chatId, ct);
        if (chat is null)
        {
            return null;
        }

        Progress progress = await AdvanceAsync(db, chat, waits, ct);
        return progress;
    }

    private async Task<Progress> AdvanceAsync(ChatsDbContext db, Chat chat, RunnerWaits waits, CancellationToken ct)
    {
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
            await StopTurnAsync(db, chat, waiting, ct);
        }

        List<Message> queued = waiting.Where(message => message.State == MessageState.Queued).ToList();
        if (queued.Count > 0 || chat.TurnMessageId is not null)
        {
            _telemetry.Working(chat);
        }

        await DriveAgentAsync(db, chat, queued, waits, outgoing, ct);

        // What failed before got this far now.
        StoredEvent? resumed = chat.WorkResumed(time);
        if (resumed is not null)
        {
            db.Events.Add(resumed);
        }

        await SaveAsync(db, ct);
        await SendAsync(chat, outgoing, ct);
        bool idle = chat.TurnMessageId is null && !waiting.Any(message => message.Waiting) && !chat.StartsAgent && !chat.AgentChangeWaiting
            && !chat.SetupRunWaiting && (chat.HarnessProcessId is null || chat.Session.SessionId is not null);
        if (!idle)
        {
            await KeepAwakeAsync(chat, ct);
        }
        else
        {
            _telemetry.Idle();
        }

        _telemetry.CloseEnded();

        return new Progress(chat.NookId, chat.HarnessProcessId, chat.OutputOffset, idle);
    }

    // Starts the agent when there's work for it, stops it between turns when another account or model
    // was chosen, or once it answered everything when the setup is to run again, and otherwise hands it
    // what waits. The setup runs again with the agent stopped, and the next agent starts after it.
    private async Task DriveAgentAsync(ChatsDbContext db, Chat chat, List<Message> queued, RunnerWaits waits, List<string> outgoing, CancellationToken ct)
    {
        bool answeredAll = chat.TurnMessageId is null && queued.Count == 0;
        if (chat.SetupRunWaiting && answeredAll && chat.HarnessProcessId is null)
        {
            await RunSetupAgainAsync(chat, ct);
        }

        if (chat.HarnessProcessId is null && (queued.Count > 0 || chat.TurnMessageId is not null || chat.StartsAgent))
        {
            bool setUp = await SetUpAsync(db, chat, queued.FirstOrDefault(), waits, ct);
            if (setUp)
            {
                await StartHarnessAsync(db, chat, queued.FirstOrDefault(), outgoing, ct);
            }
        }
        else if ((chat.AgentChangeWaiting || (chat.SetupRunWaiting && answeredAll)) && chat.TurnMessageId is null && chat.HarnessProcessId is ProcessId running)
        {
            // When the stopped agent's exit arrives, the next one starts on the account and model chosen,
            // after the setup when it runs again, and continues the conversation.
            if (_stoppedForChange != running)
            {
                _stoppedForChange = running;
                await StopHarnessAsync(chat, ct);
            }
        }
        else if (chat.Session.SessionId is not null)
        {
            await DeliverAsync(db, chat, queued, outgoing, ct);
        }
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

        Result kept = await nooks.WakeAsync(SystemActors.Harness, new WakeNook(chat.NookId, KeepAwakeFor), ct);
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
    private async Task StopTurnAsync(ChatsDbContext db, Chat chat, List<Message> waiting, CancellationToken ct)
    {
        foreach (Message message in waiting.Where(message => message.State == MessageState.Queued))
        {
            db.Events.Add(chat.Record(new ChatEventBody(new MessageCancelled(message.Id)), time));
            message.Cancel();
        }

        if (chat.TurnMessageId is not MessageId turn)
        {
            return;
        }

        if (chat.Session.SessionId is not null)
        {
            await SendAsync(chat, [chat.Session.Cancel()], ct);
        }

        await EndTurnAsync(db, chat, turn, new TurnEnded("cancelled", Failure: null), failure: null, ct);
    }

    // A turn ends with a checkpoint of the files it changed, and only then does everyone see that it
    // ended, so a turn that ended has its files saved. A failed checkpoint is told and the chat goes
    // on: the next turn's checkpoint keeps the files too. A nearly full disk is told too, since the
    // next checkpoints may fail. Then the harness state syncs.
    private async Task EndTurnAsync(ChatsDbContext db, Chat chat, MessageId turn, TurnEnded ended, string? failure, CancellationToken ct)
    {
        await SaveAsync(db, ct);
        TurnMessage message = await db.Messages.Where(found => found.Id == turn).Select(found => new TurnMessage(found.Text, found.SentBy, found.SentAt)).SingleAsync(ct);
        Result<CheckpointSummary> taken = await nooks.CheckpointAsync(SystemActors.Harness, new CheckpointNook(chat.NookId, Note(message.Text)), ct);
        ChatEventBody told = taken.Failed
            ? new ChatEventBody(new CheckpointFailed(taken.Error.Message))
            : new ChatEventBody(new CheckpointSaved(taken.Output.Number, taken.Output.Changes));
        db.Events.Add(chat.Record(told, time));
        Result<NookSummary> nook = await nooks.GetAsync(SystemActors.Harness, chat.NookId, ct);
        if (!nook.Failed && nook.Output is { DiskNearlyFull: true, Usage: NookUsage usage })
        {
            db.Events.Add(chat.Record(new ChatEventBody(new DiskNearlyFull(usage.DiskUsedBytes, usage.DiskTotalBytes)), time));
        }

        db.Events.Add(chat.Record(new ChatEventBody(ended), time));
        chat.TurnEnded();
        _agentFinishedTurn = false;
        _telemetry.TurnEnded(chat, message.SentBy, message.SentAt, ended, failure, checkpointSaved: !taken.Failed);
        await states.SyncAsync(chat, ct);
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
    private async Task DeliverAsync(ChatsDbContext db, Chat chat, List<Message> queued, List<string> outgoing, CancellationToken ct)
    {
        if (chat.TurnMessageId is null && queued.Count > 0)
        {
            Message first = queued[0];
            queued.RemoveAt(0);
            ModelAccessKind? served = await AccountServesAsync(db, chat, first, ct);
            if (served is not ModelAccessKind kind)
            {
                return;
            }

            db.Events.Add(chat.Record(new ChatEventBody(new TurnStarted(first.Id)), time));
            first.Deliver();
            chat.TurnStarted(first.Id);
            _telemetry.TurnStarted(first.SentAt, kind);
            outgoing.Add(chat.Session.Prompt(first.Id.Value, first.Text));
        }

        if (chat.TurnMessageId is not null && chat.Session.CanSteer && !_agentFinishedTurn)
        {
            foreach (Message message in queued)
            {
                message.Steer();
                outgoing.Add(chat.Session.Steer(message.Id.Value, message.Text));
            }
        }
    }

    // A running agent starts a turn only while its account serves it, as an agent starting does: once
    // the account is removed or its sign-in ended, the model gateway refuses the agent's calls, which
    // its harness retries for minutes. The message's turn fails at once with the reason instead, and
    // the agent stops. Not handled: that happening mid-turn; the turn fails once the harness gives up,
    // or when someone stops it.
    private async Task<ModelAccessKind?> AccountServesAsync(ChatsDbContext db, Chat chat, Message first, CancellationToken ct)
    {
        Result<AgentAccountCredential> used = await accounts.UseAsync(SystemActors.Harness, chat.AgentAccountId, chat.WorkspaceId, ct);
        if (!used.Failed)
        {
            return used.Output.Kind;
        }

        await FailAsync(db, chat, first, "The agent couldn't start: " + used.Error.Message, ct);
        await StopHarnessAsync(chat, ct);
        return null;
    }

    // Starts an agent with the chat, for the turn a lost one was working on, or for the first queued
    // message, once the nook's setup ended.
    private async Task StartHarnessAsync(ChatsDbContext db, Chat chat, Message? first, List<string> outgoing, CancellationToken ct)
    {
        // Each waiting message tries once, so an agent that can't start ends with every message answered.
        chat.AgentStartTried();
        // A chat started before every chat had a model starts no agent until it takes one.
        HarnessProfile? harness = HarnessProfiles.Find(chat.Harness);
        if (harness is null || chat.Model is not string model)
        {
            string reason = harness is null ? "its harness is no longer offered." : ChatsErrors.ModelNotChosen.Message;
            await FailAsync(db, chat, first, "The agent couldn't start: " + reason, ct);
            return;
        }

        Result<AgentAccountCredential> used = await accounts.UseAsync(SystemActors.Harness, chat.AgentAccountId, chat.WorkspaceId, ct);
        if (used.Failed)
        {
            await FailAsync(db, chat, first, "The agent couldn't start: " + used.Error.Message, ct);
            return;
        }

        AgentAccountCredential account = used.Output;

        // A model API stays behind the model gateway: the agent gets a token for this chat instead,
        // saved first so its first model call finds it.
        CredentialKind kind = AccountCredentials.KindOf(account.Kind);
        IReadOnlyDictionary<string, string> environment = harness.EnvironmentFor(kind, chat.IssueHarnessToken(), settings.ModelGatewayUrl, model, chat.Thinking);
        await SaveAsync(db, ct);

        // The agent starts with its instructions and its starter's harness state, after the nook's files
        // are in place. Instructions are rules people rely on, so it never starts without them.
        Result instructed = await instructions.WriteAsync(chat, harness, ct);
        if (instructed.Failed)
        {
            await FailAsync(db, chat, first, "The agent couldn't start: writing its instructions failed: " + instructed.Error.Message, ct);
            return;
        }

        await states.SyncAsync(chat, ct);
        Result<ProcessId> started = await agent.StartAsync(chat.NookId, environment, ct);
        if (started.Failed)
        {
            await FailAsync(db, chat, first, "The agent couldn't start: " + started.Error.Message, ct);
            return;
        }

        chat.HarnessStarted(started.Output);
        outgoing.Add(AgentSession.Start(ClientName, ClientTitle, ClientVersion));
    }

    // The agent starts once its nook's setup ended. Putting the nook's files in place starts it, and the
    // chat tells when it started and how it ended. Waiting holds up nothing else: a watch wakes the
    // runner when the run ends. Returns whether the agent may start; a nook whose setup can't be had
    // fails the start.
    private async Task<bool> SetUpAsync(ChatsDbContext db, Chat chat, Message? first, RunnerWaits waits, CancellationToken ct)
    {
        Result<NookSetup> setup = await nooks.GetSetupAsync(SystemActors.Harness, chat.NookId, ct);

        if (setup.Failed)
        {
            await FailAsync(db, chat, first, "The agent couldn't start: " + setup.Error.Message, ct);
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
        _telemetry.SetupEnded(chat, ended.ExitCode, took, setup.Output.Scripts.Count, fromReadyCopy: setup.Output.ReadyCopyMadeAt is not null);
        return true;
    }

    // Starts the nook's setup again, as asked; the next agent starts once it ended, as after the first
    // run. A run already going, such as a woken nook's resume, takes the place of the one asked for.
    private async Task RunSetupAgainAsync(Chat chat, CancellationToken ct)
    {
        Result<NookSetup> again = await nooks.RunSetupAsync(SystemActors.Harness, chat.NookId, ct);

        // Not handled: a nook that can't run it now, as one gone or out of reach, starts the agent
        // without it, and the log says why; the person can ask again.
        if (again.Failed && again.Error != NooksErrors.SetupRunning)
        {
            Log.SetupNotRunAgain(logger, chatId.Value, again.Error.Message);
        }

        chat.SetupRunAgain();
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

    // Returns true when the agent's process ended, or the chat is gone.
    private async Task<bool> HandleAsync(List<RunnerInput> batch, CancellationToken ct)
    {
        Activity.Current = _telemetry.Turn;
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);
        Chat? chat = await db.Chats.SingleOrDefaultAsync(found => found.Id == chatId, ct);
        if (chat is null)
        {
            return true;
        }

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
        _telemetry.CloseEnded();
        return exited;
    }

    // What the agent's line came to: its session answers what needs no chat (AgentSession), and the
    // chat records progress, takes over a lost agent's turn, and ends turns.
    private async Task HandleLineAsync(ChatsDbContext db, Chat chat, string line, List<string> outgoing, CancellationToken ct)
    {
        AgentStep step = chat.Session.Read(line, AgentProcess.WorkingDirectory);
        chat.SessionChanged(step.Session);
        outgoing.AddRange(step.Lines);
        if (step.Event is not AgentEvent happened)
        {
            return;
        }

        switch (happened)
        {
            // Not handled: merging streamed text chunks, each of which is a row.
            case AgentReported reported:
                db.Events.Add(chat.Record(new ChatEventBody(new AgentUpdate(reported.Update)), time));
                _telemetry.Reported(chat, reported);
                break;
            case SessionReady ready:
                _telemetry.SessionReady(ready.Model);
                if (ready.Remembers is bool remembers)
                {
                    db.Events.Add(chat.Record(new ChatEventBody(new AgentRestarted(remembers)), time));
                }

                await CarryOverTurnAsync(db, chat, outgoing, ct);
                break;
            case StartFailed failed:
                await FailStartAsync(db, chat, failed.Error, ct);
                break;
            case PromptAnswered answered:
                TurnEnded ended = answered.Error is null ? new TurnEnded(answered.StopReason, Failure: null) : new TurnEnded("failed", answered.Error);
                await PromptEndedAsync(db, chat, MessageId.From(answered.Prompt), ended, ct);
                break;
            case SteerAnswered answered:
                await HandleSteerAnswerAsync(db, chat, MessageId.From(answered.Message), answered.Joined, ct);
                break;
        }
    }

    // The agent can't be used: the turn a lost one was working on, or the first waiting message,
    // gets the answer, and the process goes.
    private async Task FailStartAsync(ChatsDbContext db, Chat chat, string error, CancellationToken ct)
    {
        Message? first = await db.Messages.Where(message => message.ChatId == chatId && message.State == MessageState.Queued).OrderBy(message => message.Id).FirstOrDefaultAsync(ct);
        await FailAsync(db, chat, first, "The agent couldn't start: " + error, ct);
        _startFailed = true;
        await StopHarnessAsync(chat, ct);
    }

    // A new agent's session is ready while a turn runs: the turn a lost agent was working on, whose
    // message it gets again.
    private static async Task CarryOverTurnAsync(ChatsDbContext db, Chat chat, List<string> outgoing, CancellationToken ct)
    {
        if (chat.TurnMessageId is not MessageId turn || chat.Session.SessionId is null)
        {
            return;
        }

        string text = await db.Messages.Where(message => message.Id == turn).Select(message => message.Text).SingleAsync(ct);
        outgoing.Add(chat.Session.Prompt(turn.Value, text));
    }

    // A stopped turn already ended; its late answer changes nothing.
    private async Task PromptEndedAsync(ChatsDbContext db, Chat chat, MessageId prompt, TurnEnded ended, CancellationToken ct)
    {
        if (chat.TurnMessageId == prompt)
        {
            string? failure = string.Equals(ended.StopReason, "failed", StringComparison.Ordinal) ? "agent_error" : null;
            await EndTurnAsync(db, chat, prompt, ended, failure, ct);
        }
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
    // nook's latest checkpoint; any other exit ends the turn as failed, and one before the agent had a
    // session, as its start script failing, fails the first message waiting for it.
    private async Task HandleExitAsync(ChatsDbContext db, Chat chat, int exitCode, CancellationToken ct)
    {
        bool lost = exitCode == ProcessExited.Lost;
        if (chat.TurnMessageId is MessageId turn && !lost)
        {
            string failure = string.Create(CultureInfo.InvariantCulture, $"The agent stopped with exit code {exitCode}.");
            await EndTurnAsync(db, chat, turn, new TurnEnded("failed", failure), "agent_exited", ct);
        }
        else if (!lost && chat.Session.SessionId is null && !_startFailed)
        {
            Message? first = await db.Messages.Where(message => message.ChatId == chatId && message.State == MessageState.Queued).OrderBy(message => message.Id).FirstOrDefaultAsync(ct);
            await FailAsync(db, chat, first, string.Create(CultureInfo.InvariantCulture, $"The agent couldn't start: it stopped with exit code {exitCode}."), ct);
        }

        List<Message> steering = await db.Messages.Where(message => message.ChatId == chatId && message.State == MessageState.Steering).ToListAsync(ct);
        foreach (Message message in steering)
        {
            message.Queue();
        }

        // The next agent loads this one's session when it can, on the account and model chosen meanwhile.
        if (lost)
        {
            chat.HarnessLost();
        }
        else
        {
            chat.HarnessStopped();
        }

        chat.ApplyAgentChange();
        _stoppedForChange = null;
        _agentFinishedTurn = false;
        _startFailed = false;
    }

    // The turn a lost agent was working on ends as failed; otherwise the message gets a turn that
    // fails at once.
    private async Task FailAsync(ChatsDbContext db, Chat chat, Message? message, string failure, CancellationToken ct)
    {
        if (chat.TurnMessageId is MessageId turn)
        {
            await EndTurnAsync(db, chat, turn, new TurnEnded("failed", failure), "start", ct);
        }
        else if (message is not null)
        {
            TurnEnded failed = new TurnEnded("failed", failure);
            db.Events.Add(chat.Record(new ChatEventBody(new TurnStarted(message.Id)), time));
            db.Events.Add(chat.Record(new ChatEventBody(failed), time));
            message.Deliver();
            _telemetry.TurnEnded(chat, message.SentBy, message.SentAt, failed, "start", checkpointSaved: null);
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

        // Lists show whether the agent works: whether a turn runs, or messages wait for one.
        EntityEntry<Chat>? chat = db.ChangeTracker.Entries<Chat>().SingleOrDefault();
        WorkspaceId? listChanged = chat is not null
            && (chat.Property(found => found.TurnMessageId).IsModified || db.ChangeTracker.Entries<Message>().Any(entry => entry.State is EntityState.Added or EntityState.Modified))
            ? chat.Entity.WorkspaceId
            : null;
        // People change the chat too (ChatsApi.ChangeChatAsync): one who saved first shows as a conflict,
        // or as an event already saved with the sequence number the runner took. The runner then does
        // its work again from what's saved.
        Result saved = await db.SaveAsync(ct);
        bool changedMeanwhile = saved.Error == ModuleDbContextExtensions.ConcurrencyConflict || saved.Error == ModuleDbContextExtensions.AlreadyExists;
        if (changedMeanwhile)
        {
            throw new DbUpdateConcurrencyException("Chat " + chatId.Value + " changed while its runner worked on it.");
        }

        if (saved.Failed)
        {
            throw new InvalidOperationException("Saving chat " + chatId.Value + " failed: " + saved.Error.Message);
        }

        if (recorded)
        {
            signals.Notify(chatId);
        }

        if (listChanged is WorkspaceId workspace)
        {
            signals.Notify(workspace);
        }
    }

    private sealed record Progress(NookId NookId, ProcessId? Harness, long OutputOffset, bool Idle);

    // The message a turn answered: its first line names the checkpoint, and it says whose turn it was.
    private sealed record TurnMessage(string Text, UserId SentBy, DateTimeOffset SentAt);
}
