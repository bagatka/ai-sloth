using System;
using System.Collections.Generic;
using System.Diagnostics;
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
using Microsoft.Extensions.Logging;

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

    private static readonly ActivitySource Traces = new ActivitySource("Bagatka.AiSloth.Chats");

    private readonly Channel<RunnerInput> _inputs = Channel.CreateBounded<RunnerInput>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    // The agent answered a steer with promptRequired: its turn is over even if its answer to the prompt
    // isn't read yet, so nothing more is offered to it until the turn ends here too.
    private bool _agentFinishedTurn;
    private int _stopRequested;

    // The trace of the turn the chat works on, from when a message waits for the agent, its start
    // included, to the turn's end: a trace of its own, current only in the steps that work on the
    // turn, so the runner's background reading and waiting stay out of it. The step that ends the
    // turn closes its trace once it saved the end.
    private Activity? _turn;
    private bool _turnEnded;

    // When the message of the running turn was sent, until its agent first did something for it; then
    // how long that took.
    private DateTimeOffset? _firstActionDue;
    private TimeSpan? _firstActionWaited;

    // The model that last answered, or else the one the agent's session started with, when the agent
    // said, and the kind of account it runs on, as its last turn found it, for product analytics.
    // A session's model can be "default", the agent's own choice, until a model answers.
    private string? _model;
    private AgentAccountKind? _accountKind;

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
            _turn?.Dispose();
        }
    }

    // Works until the agent's process ends, or the runner retires; returns whether it retired.
    private async Task<bool> RunOnceAsync(Func<ChatRunner, bool> retire, Waits waits, CancellationToken ct)
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
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            // A draft that went with its nook fails what its runner was doing; the next try retires it.
            await using (ChatsDbContext db = await databases.CreateDbContextAsync(ct))
            {
                bool exists = await db.Chats.AnyAsync(found => found.Id == chatId, ct);
                if (exists)
                {
                    Log.RunnerFailed(logger, exception, chatId.Value);
                }
            }

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
    private async Task<Progress?> AdvanceAsync(Waits waits, CancellationToken ct)
    {
        Activity.Current = _turn;
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);
        Chat? chat = await db.Chats.SingleOrDefaultAsync(found => found.Id == chatId, ct);
        if (chat is null)
        {
            return null;
        }

        Progress progress = await AdvanceAsync(db, chat, waits, ct);
        return progress;
    }

    private async Task<Progress> AdvanceAsync(ChatsDbContext db, Chat chat, Waits waits, CancellationToken ct)
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
        if (_turn is null && (queued.Count > 0 || chat.TurnMessageId is not null))
        {
            _turn = Traces.StartActivity("turn");
            _turn?.SetTag("chat.id", chatId.Value.ToString("D", CultureInfo.InvariantCulture));
            _turn?.SetTag("harness", chat.Harness);
        }

        if (chat.HarnessProcessId is null && (queued.Count > 0 || chat.TurnMessageId is not null || chat.StartsAgent))
        {
            bool setUp = await SetUpAsync(db, chat, queued.FirstOrDefault(), waits, ct);
            if (setUp)
            {
                await StartHarnessAsync(db, chat, queued.FirstOrDefault(), outgoing, ct);
            }
        }
        else if (chat.SessionId is not null)
        {
            await DeliverAsync(db, chat, chat.SessionId, queued, outgoing, ct);
        }

        await SaveAsync(db, ct);
        await SendAsync(chat, outgoing, ct);
        bool idle = chat.TurnMessageId is null && !waiting.Any(message => message.Waiting) && !chat.StartsAgent
            && (chat.HarnessProcessId is null || chat.SessionId is not null);
        if (!idle)
        {
            await KeepAwakeAsync(chat, ct);
        }
        else if (_turn is not null && !_turnEnded)
        {
            // Stopped before the agent took the message.
            EndTurnTrace("cancelled", failure: null);
        }

        CloseEndedTurnTrace();

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

        if (chat.SessionId is not null)
        {
            await SendAsync(chat, [Acp.Cancel(chat.SessionId)], ct);
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
            : new ChatEventBody(new CheckpointSaved(taken.Output.Number));
        db.Events.Add(chat.Record(told, time));
        Result<NookSummary> nook = await nooks.GetAsync(SystemActors.Harness, chat.NookId, ct);
        if (!nook.Failed && nook.Output is { DiskNearlyFull: true, Usage: NookUsage usage })
        {
            db.Events.Add(chat.Record(new ChatEventBody(new DiskNearlyFull(usage.DiskUsedBytes, usage.DiskTotalBytes)), time));
        }

        db.Events.Add(chat.Record(new ChatEventBody(ended), time));
        chat.TurnEnded();
        _agentFinishedTurn = false;
        CaptureTurnEnded(chat, message.SentBy, message.SentAt, ended, failure, checkpointSaved: !taken.Failed);
        await states.SyncAsync(chat, ct);
        EndTurnTrace(ended.StopReason, failure);
    }

    // The turn's trace ends with how it ended, and for a failed one where it failed.
    private void EndTurnTrace(string outcome, string? failure)
    {
        _turn?.SetTag("outcome", outcome);
        if (failure is not null)
        {
            _turn?.SetStatus(ActivityStatusCode.Error, failure);
        }

        _turnEnded = true;
    }

    private void CloseEndedTurnTrace()
    {
        if (!_turnEnded)
        {
            return;
        }

        _turn?.Dispose();
        _turn = null;
        _turnEnded = false;
    }

    // What a turn came to, for product analytics: how it ended, and for a failed one where it failed
    // (start: the agent couldn't start; agent_error: it answered with an error, such as its model's;
    // agent_exited: it stopped), never the failure's words, which can quote a vendor; how long its
    // message waited for that, and for the agent's first action, when this runner saw it; the model,
    // when the agent said; and whether the turn's files were saved, for a turn that had any.
    private void CaptureTurnEnded(Chat chat, UserId sentBy, DateTimeOffset sentAt, TurnEnded ended, string? failure, bool? checkpointSaved)
    {
        Dictionary<string, ProductFact> facts = new Dictionary<string, ProductFact>(StringComparer.Ordinal)
        {
            ["harness"] = new ProductFact(chat.Harness),
            ["outcome"] = new ProductFact(ended.StopReason),
            ["seconds"] = new ProductFact((time.GetUtcNow() - sentAt).TotalSeconds),
        };
        if (failure is not null)
        {
            facts["failure"] = new ProductFact(failure);
        }

        if (_firstActionWaited is TimeSpan waited)
        {
            facts["first_action_seconds"] = new ProductFact(waited.TotalSeconds);
        }

        if (_model is not null)
        {
            facts["model"] = new ProductFact(_model);
        }

        if (_accountKind is AgentAccountKind kind)
        {
            facts["account_kind"] = new ProductFact(kind.ToString());
        }

        if (checkpointSaved is bool saved)
        {
            facts["checkpoint_saved"] = new ProductFact(saved);
        }

        productEvents.Capture(new ProductEvent("turn_ended", sentBy, chat.WorkspaceId.Value, facts));
        _firstActionWaited = null;
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
    private async Task DeliverAsync(ChatsDbContext db, Chat chat, string sessionId, List<Message> queued, List<string> outgoing, CancellationToken ct)
    {
        if (chat.TurnMessageId is null && queued.Count > 0)
        {
            Message first = queued[0];
            queued.RemoveAt(0);
            bool served = await AccountServesAsync(db, chat, first, ct);
            if (!served)
            {
                return;
            }

            db.Events.Add(chat.Record(new ChatEventBody(new TurnStarted(first.Id)), time));
            first.Deliver();
            chat.TurnStarted(first.Id);
            _firstActionDue = first.SentAt;
            _firstActionWaited = null;
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

    // A running agent starts a turn only while its account serves it, as an agent starting does: once
    // the account is removed or its sign-in ended, the model gateway refuses the agent's calls, which
    // its harness retries for minutes. The message's turn fails at once with the reason instead, and
    // the agent stops. Not handled: that happening mid-turn; the turn fails once the harness gives up,
    // or when someone stops it.
    private async Task<bool> AccountServesAsync(ChatsDbContext db, Chat chat, Message first, CancellationToken ct)
    {
        Result<AgentAccountCredential> used = await accounts.UseAsync(SystemActors.Harness, chat.AgentAccountId, chat.WorkspaceId, ct);
        if (!used.Failed)
        {
            _accountKind = used.Output.Kind;
            return true;
        }

        await FailAsync(db, chat, first, "The agent couldn't start: " + used.Error.Message, ct);
        await StopHarnessAsync(chat, ct);
        return false;
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
            await FailAsync(db, chat, first, "The agent couldn't start: its harness is no longer offered.", ct);
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
        outgoing.Add(Acp.Initialize(ClientName, ClientTitle, ClientVersion));
    }

    // The agent starts once its nook's setup ended. Putting the nook's files in place starts it, and the
    // chat tells when it started and how it ended. Waiting holds up nothing else: a watch wakes the
    // runner when the run ends. Returns whether the agent may start; a nook whose setup can't be had
    // fails the start.
    private async Task<bool> SetUpAsync(ChatsDbContext db, Chat chat, Message? first, Waits waits, CancellationToken ct)
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
        productEvents.Capture(new ProductEvent("setup_ended", chat.StartedBy, chat.WorkspaceId.Value, new Dictionary<string, ProductFact>(StringComparer.Ordinal)
        {
            ["succeeded"] = new ProductFact(ended.ExitCode == 0),
            ["seconds"] = new ProductFact(took.TotalSeconds),
            ["scripts"] = new ProductFact(setup.Output.Scripts.Count),
            ["from_ready_copy"] = new ProductFact(setup.Output.ReadyCopyMadeAt is not null),
        }));
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

    // Returns true when the agent's process ended, or the chat is gone.
    private async Task<bool> HandleAsync(List<RunnerInput> batch, CancellationToken ct)
    {
        Activity.Current = _turn;
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
        CloseEndedTurnTrace();
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
            // Not handled: merging streamed text chunks, each of which is a row.
            case AcpUpdate update when !chat.LoadingSession:
                db.Events.Add(chat.Record(new ChatEventBody(new AgentUpdate(update.Update)), time));
                MeasureFirstAction(chat, update);
                _model = update.Model ?? _model;
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
            case AcpSessionLoaded loaded:
                _model = loaded.Model;
                chat.SessionLoaded();
                db.Events.Add(chat.Record(new ChatEventBody(new AgentRestarted(Remembers: true)), time));
                await CarryOverTurnAsync(db, chat, outgoing, ct);
                break;
            case AcpLoadFailed:
                chat.LoadFailed();
                outgoing.Add(Acp.NewSession(AgentProcess.WorkingDirectory));
                break;
            case AcpSessionCreated created:
                _model = created.Model;
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
                await PromptEndedAsync(db, chat, MessageId.From(ended.Prompt), new TurnEnded(ended.StopReason, Failure: null), ct);
                break;
            case AcpPromptFailed failed:
                await PromptEndedAsync(db, chat, MessageId.From(failed.Prompt), new TurnEnded("failed", failed.Error), ct);
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
            _firstActionWaited = time.GetUtcNow() - sentAt;
            meter.FirstAction(chat.Harness, _firstActionWaited.Value);
            _firstActionDue = null;
        }
    }

    // The agent can't be used: the turn a lost one was working on, or the first waiting message,
    // gets the answer, and the process goes.
    private async Task FailStartAsync(ChatsDbContext db, Chat chat, string error, CancellationToken ct)
    {
        Message? first = await db.Messages.Where(message => message.ChatId == chatId && message.State == MessageState.Queued).OrderBy(message => message.Id).FirstOrDefaultAsync(ct);
        await FailAsync(db, chat, first, "The agent couldn't start: " + error, ct);
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
    // nook's latest checkpoint; any other exit ends the turn as failed.
    private async Task HandleExitAsync(ChatsDbContext db, Chat chat, int exitCode, CancellationToken ct)
    {
        bool lost = exitCode == ProcessExited.Lost;
        if (chat.TurnMessageId is MessageId turn && !lost)
        {
            string failure = string.Create(CultureInfo.InvariantCulture, $"The agent stopped with exit code {exitCode}.");
            await EndTurnAsync(db, chat, turn, new TurnEnded("failed", failure), "agent_exited", ct);
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
            CaptureTurnEnded(chat, message.SentBy, message.SentAt, failed, "start", checkpointSaved: null);
            EndTurnTrace(failed.StopReason, "start");
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
    // agent waits for, and a regular tick that has the runner look at its chat again.
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

        public async ValueTask DisposeAsync()
        {
            await SetupRun.StopAsync();
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

    // The message a turn answered: its first line names the checkpoint, and it says whose turn it was.
    private sealed record TurnMessage(string Text, UserId SentBy, DateTimeOffset SentAt);
}
