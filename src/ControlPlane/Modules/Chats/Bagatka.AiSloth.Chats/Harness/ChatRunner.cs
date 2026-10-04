using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Chats.Harness;

// Talks to one chat's agent over ACP on its process's standard input and output: starts it, hands it
// messages, answers its requests, and saves what it reports. ChatRunners keeps at most one runner per
// chat. A runner exits when its chat is idle; how far the agent's output was read is saved with every
// change, so the next runner, even after a restart, continues exactly there.
internal sealed class ChatRunner(
    ChatId chatId,
    IDbContextFactory<ChatsDbContext> databases,
    IServiceScopeFactory scopes,
    ChatsSettings settings,
    ChatSignals signals,
    TimeProvider time,
    ILogger logger)
{
    private const int MaxBatch = 200;

    // SendInput's limit, and the longest line an agent may write before something is clearly off.
    private const int MaxInputBytes = 64 * 1024;
    private const int MaxLineBytes = 32 * 1024 * 1024;

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);
    private static readonly Actor Harness = Actor.ForSystem("chats.harness");

    private readonly Channel<RunnerInput> _inputs = Channel.CreateBounded<RunnerInput>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    // Messages the agent answered promptRequired for in the running turn; they start the next one.
    private readonly HashSet<MessageId> _notSteerable = [];
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
                if (progress.Harness is ProcessId process)
                {
                    if (await ServeAsync(progress, process, retire, ct))
                    {
                        return;
                    }
                }
                else if (progress.Idle && retire(this))
                {
                    return;
                }
                else
                {
                    await HandleAsync(await NextBatchAsync(ct), ct);
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
        await using Reading reading = new Reading(token => ReadAsync(nookId, process, offset, token), ct);
        while (true)
        {
            if (progress.Idle && retire(this))
            {
                return true;
            }

            if (await HandleAsync(await NextBatchAsync(ct), ct))
            {
                return false;
            }

            progress = await AdvanceAsync(ct);
            if (progress.Harness != process)
            {
                return false;
            }
        }
    }

    private async Task<List<RunnerInput>> NextBatchAsync(CancellationToken ct)
    {
        List<RunnerInput> batch = [await _inputs.Reader.ReadAsync(ct)];
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

        if (Interlocked.Exchange(ref _stopRequested, 0) == 1)
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
        else if (chat.SessionId is string sessionId)
        {
            Deliver(db, chat, sessionId, queued, outgoing);
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
            _notSteerable.Clear();
            if (chat.SessionId is string sessionId)
            {
                outgoing.Add(Acp.Cancel(sessionId));
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
            outgoing.Add(Acp.Prompt(sessionId, first.Id, first.Text));
        }

        if (chat.TurnMessageId is not null && chat.SupportsSteering)
        {
            foreach (Message message in queued.Where(message => !_notSteerable.Contains(message.Id)))
            {
                message.Steer();
                outgoing.Add(Acp.Steer(sessionId, message.Id, message.Text));
            }
        }
    }

    private async Task StartHarnessAsync(ChatsDbContext db, Chat chat, Message first, List<string> outgoing, CancellationToken ct)
    {
        // Saved first, so the agent's first model call finds its token.
        string token = chat.IssueHarnessToken();
        await SaveAsync(db, ct);

        StartProcess start = new StartProcess(
            chat.NookId,
            ClaudeCodeHarness.Command,
            [],
            ClaudeCodeHarness.WorkingDirectory,
            OutputRetention.Complete,
            ClaudeCodeHarness.Environment(settings.ModelGatewayUrl, token));
        Result<ProcessSummary> started;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            started = await scope.ServiceProvider.GetRequiredService<INooksApi>().StartProcessAsync(Harness, start, ct);
        }

        if (started.TryGetValue(out ProcessSummary? process, out Error? error))
        {
            chat.HarnessStarted(process.Id);
            outgoing.Add(Acp.Initialize());
            return;
        }

        // Each waiting message tries once, so a nook that can't run agents ends with every message answered.
        Fail(db, chat, first, "The agent couldn't start: " + error.Message);
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
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            // Not a protocol message; agents log to standard error, so this is noise.
            return;
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            bool isCall = root.TryGetProperty("method", out JsonElement method);
            bool hasId = root.TryGetProperty("id", out JsonElement id);
            if (isCall && hasId)
            {
                outgoing.Add(string.Equals(method.GetString(), "session/request_permission", StringComparison.Ordinal)
                    ? Acp.Allow(id, root.GetProperty("params").GetProperty("options"))
                    : Acp.MethodNotFound(id));
            }
            else if (isCall)
            {
                if (string.Equals(method.GetString(), "session/update", StringComparison.Ordinal)
                    && root.TryGetProperty("params", out JsonElement parameters) && parameters.TryGetProperty("update", out JsonElement update))
                {
                    db.Events.Add(chat.Record(new ChatEventBody(new AgentUpdate(update.Clone())), time));
                }
            }
            else if (hasId && id.ValueKind == JsonValueKind.String)
            {
                await HandleResponseAsync(db, chat, id.GetString()!, root, outgoing, ct);
            }
        }
    }

    private async Task HandleResponseAsync(ChatsDbContext db, Chat chat, string id, JsonElement response, List<string> outgoing, CancellationToken ct)
    {
        bool failed = response.TryGetProperty("error", out JsonElement error);
        response.TryGetProperty("result", out JsonElement result);
        if (string.Equals(id, Acp.InitializeId, StringComparison.Ordinal) || string.Equals(id, Acp.NewSessionId, StringComparison.Ordinal))
        {
            if (failed)
            {
                // The agent can't be used: the first waiting message gets the answer, and the process goes.
                Message? first = await db.Messages.Where(message => message.ChatId == chatId && message.State == MessageState.Queued).OrderBy(message => message.Id).FirstOrDefaultAsync(ct);
                if (first is not null)
                {
                    Fail(db, chat, first, "The agent couldn't start: " + ErrorText(error));
                }

                await StopHarnessAsync(chat, ct);
            }
            else if (string.Equals(id, Acp.InitializeId, StringComparison.Ordinal))
            {
                chat.Initialized(SupportsSteering(result));
                outgoing.Add(Acp.NewSession());
            }
            else
            {
                chat.SessionReady(result.GetProperty("sessionId").GetString()!);
            }
        }
        else if (Acp.PromptMessage(id) is MessageId prompted)
        {
            // A stopped turn already ended; its late answer changes nothing.
            if (chat.TurnMessageId == prompted)
            {
                TurnEnded ended = failed
                    ? new TurnEnded("failed", ErrorText(error))
                    : new TurnEnded(result.GetProperty("stopReason").GetString()!, Failure: null);
                db.Events.Add(chat.Record(new ChatEventBody(ended), time));
                chat.TurnEnded();
                _notSteerable.Clear();
            }
        }
        else if (Acp.SteeredMessage(id) is MessageId steered)
        {
            Message? message = await db.Messages.SingleOrDefaultAsync(found => found.Id == steered && found.State == MessageState.Steering, ct);
            if (message is not null && !failed && string.Equals(result.GetProperty("outcome").GetString(), "injected", StringComparison.Ordinal))
            {
                message.Deliver();
                db.Events.Add(chat.Record(new ChatEventBody(new MessageSteered(message.Id)), time));
            }
            else if (message is not null)
            {
                // No running turn to join: it starts the next one.
                message.Queue();
                _notSteerable.Add(message.Id);
            }
        }
    }

    private async Task HandleExitAsync(ChatsDbContext db, Chat chat, int exitCode, CancellationToken ct)
    {
        if (chat.TurnMessageId is not null)
        {
            string failure = string.Create(CultureInfo.InvariantCulture, $"The agent stopped with exit code {exitCode}.");
            db.Events.Add(chat.Record(new ChatEventBody(new TurnEnded("failed", failure)), time));
        }

        foreach (Message message in await db.Messages.Where(message => message.ChatId == chatId && message.State == MessageState.Steering).ToListAsync(ct))
        {
            message.Queue();
        }

        // Not handled: resuming the conversation in the next agent (session/load); the next message
        // starts a new session.
        chat.HarnessStopped();
        _notSteerable.Clear();
    }

    private void Fail(ChatsDbContext db, Chat chat, Message message, string failure)
    {
        db.Events.Add(chat.Record(new ChatEventBody(new TurnStarted(message.Id)), time));
        db.Events.Add(chat.Record(new ChatEventBody(new TurnEnded("failed", failure)), time));
        message.Deliver();
        Log.AgentFailed(logger, chatId.Value, failure);
    }

    // Reads the agent's standard output from an offset and hands each complete line to the runner.
    private async Task ReadAsync(NookId nookId, ProcessId processId, long offset, CancellationToken ct)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            Result<IAsyncEnumerable<ProcessEvent>> watched = await scope.ServiceProvider.GetRequiredService<INooksApi>()
                .WatchProcessAsync(Harness, new WatchProcess(nookId, processId, offset), ct);
            if (!watched.TryGetValue(out IAsyncEnumerable<ProcessEvent>? events, out Error? error))
            {
                // A nook that is gone takes its processes with it; one that isn't ready may be back later.
                await _inputs.Writer.WriteAsync(error == NooksErrors.NotReady
                    ? new RunnerInput(new ReaderFailed(new InvalidOperationException(error.Message)))
                    : new RunnerInput(new HarnessExited(-1)), ct);
                return;
            }

            ArrayBufferWriter<byte> pending = new ArrayBufferWriter<byte>();
            await foreach (ProcessEvent processEvent in events.WithCancellation(ct))
            {
                switch (processEvent)
                {
                    case ProcessOutput output when output.Channel == OutputChannel.StandardOutput:
                        ReadOnlyMemory<byte> data = output.Data;
                        int newline;
                        while ((newline = data.Span.IndexOf((byte)'\n')) >= 0)
                        {
                            pending.Write(data.Span[..newline]);
                            long end = output.Offset + (output.Data.Length - data.Length) + newline + 1;
                            await _inputs.Writer.WriteAsync(new RunnerInput(new HarnessLine(Encoding.UTF8.GetString(pending.WrittenSpan), end)), ct);
                            pending.ResetWrittenCount();
                            data = data[(newline + 1)..];
                        }

                        pending.Write(data.Span);
                        if (pending.WrittenCount > MaxLineBytes)
                        {
                            throw new InvalidOperationException("The agent of chat " + chatId.Value + " wrote a line longer than 32 MiB.");
                        }

                        break;
                    case ProcessOutput:
                        // Standard error is the agent's own log; it may hold anything, so it isn't kept.
                        break;
                    case ProcessExited exited:
                        await _inputs.Writer.WriteAsync(new RunnerInput(new HarnessExited(exited.ExitCode)), ct);
                        return;
                }
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
        if (lines.Count == 0 || chat.HarnessProcessId is not ProcessId processId)
        {
            return;
        }

        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        INooksApi nooks = scope.ServiceProvider.GetRequiredService<INooksApi>();
        foreach (string line in lines)
        {
            ReadOnlyMemory<byte> bytes = Encoding.UTF8.GetBytes(line + "\n");
            for (int start = 0; start < bytes.Length; start += MaxInputBytes)
            {
                ReadOnlyMemory<byte> chunk = bytes[start..Math.Min(bytes.Length, start + MaxInputBytes)];
                if ((await nooks.SendInputAsync(Harness, new SendInput(chat.NookId, processId, chunk), ct)).IsError(out Error? error))
                {
                    // The process is gone or out of reach; its exit arrives through the reader.
                    Log.InputFailed(logger, chatId.Value, error.Message);
                    return;
                }
            }
        }
    }

    private async Task StopHarnessAsync(Chat chat, CancellationToken ct)
    {
        if (chat.HarnessProcessId is ProcessId processId)
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<INooksApi>().StopProcessAsync(Harness, new StopProcess(chat.NookId, processId), ct);
        }
    }

    private async Task SaveAsync(ChatsDbContext db, CancellationToken ct)
    {
        bool recorded = db.ChangeTracker.Entries<StoredEvent>().Any(entry => entry.State == EntityState.Added);
        if ((await db.SaveAsync(ct)).IsError(out Error? error))
        {
            // The runner is the chat's only writer, so a conflict is a defect.
            throw new InvalidOperationException("Saving chat " + chatId.Value + " failed: " + error.Message);
        }

        if (recorded)
        {
            signals.Notify(chatId);
        }
    }

    private static bool SupportsSteering(JsonElement initialized)
    {
        return initialized.TryGetProperty("_meta", out JsonElement meta)
            && meta.TryGetProperty("steering", out JsonElement steering)
            && steering.TryGetProperty("supported", out JsonElement supported)
            && supported.ValueKind == JsonValueKind.True;
    }

    private static string ErrorText(JsonElement error)
    {
        return error.TryGetProperty("message", out JsonElement message) && message.GetString() is string text ? text : "The agent reported an error.";
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
