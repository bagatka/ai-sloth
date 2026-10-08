using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Secrets.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

// Processes in nooks as the control plane sees them: recorded before they start, so people see
// everything that ran in their nook; their output relayed from the daemon; their exit recorded; and
// the control plane's own scripts run to their end, such as copying files in and out.
internal sealed class NookProcesses(IDbContextFactory<NooksDbContext> databases, DaemonConnections daemons, InputFeeds feeds, ISecretsApi secrets, TimeProvider time) : IDisposable
{
    public static readonly IReadOnlyDictionary<string, string> NoVariables = new Dictionary<string, string>(StringComparer.Ordinal);

    // How much of what a run prints to standard error is kept: enough to say why it failed.
    private const int MaxKeptErrors = 4000;

    // How many runs hold output at once on this instance: large outputs land on its disk, which holds
    // two of them beside the copies of repositories Sources makes; small ones are read into memory.
    // Others wait their turn.
    private readonly SemaphoreSlim _largeOutputs = new SemaphoreSlim(2, 2);
    private readonly SemaphoreSlim _smallOutputs = new SemaphoreSlim(8, 8);

    public void Dispose()
    {
        _largeOutputs.Dispose();
        _smallOutputs.Dispose();
    }

    // Every process a person or a setup starts gets the workspace's secrets, as they are when it starts;
    // its own variables win. The control plane's scripts get none.
    public async Task<IReadOnlyDictionary<string, string>> EnvironmentAsync(Nook nook, IReadOnlyDictionary<string, string> own, CancellationToken ct)
    {
        Result<IReadOnlyDictionary<string, string>> resolved = await secrets.ResolveAsync(SystemActors.Processes, nook.WorkspaceId, ct);
        if (resolved.Failed)
        {
            throw new InvalidOperationException("Resolving the secrets of nook " + nook.Id.Value + "'s workspace failed: " + resolved.Error.Message);
        }

        Dictionary<string, string> environment = new Dictionary<string, string>(resolved.Output, StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> variable in own)
        {
            environment[variable.Key] = variable.Value;
        }

        return environment;
    }

    // Records a process, in a database context of its own, before it starts.
    public async Task<Process> RecordAsync(StartProcess command, CancellationToken ct)
    {
        Result<Process> started = Process.Start(command, time);
        if (started.Failed)
        {
            throw new InvalidOperationException("A process the control plane starts in nooks is invalid: " + started.Error.Message);
        }

        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        db.Processes.Add(started.Output);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            throw new InvalidOperationException("Recording a process the control plane starts failed: " + saved.Error.Message);
        }

        return started.Output;
    }

    // Runs a script (Scripts) in a nook to its end. Its standard input comes from `input`, which the
    // daemon reads on its own stream; its standard output goes to `output`, up to its limit, past which
    // the run stops and fails. A writer of input that fails throws here.
    public async Task<ProcessRun> RunAsync(
        DaemonConnection connection,
        string script,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        Func<Stream, CancellationToken, Task>? input,
        ScriptOutput? output,
        CancellationToken ct)
    {
        SemaphoreSlim? slots = output is null ? null : output.Large ? _largeOutputs : _smallOutputs;
        if (slots is not null)
        {
            await slots.WaitAsync(ct);
        }

        try
        {
            return await RunHoldingAsync(connection, script, arguments, environment, input, output, ct);
        }
        finally
        {
            slots?.Release();
        }
    }

    private async Task<ProcessRun> RunHoldingAsync(
        DaemonConnection connection,
        string script,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        Func<Stream, CancellationToken, Task>? input,
        ScriptOutput? output,
        CancellationToken ct)
    {
        StartProcess command = new StartProcess(connection.NookId, "/bin/sh", ["-c", script, "sh", .. arguments], WorkingDirectory: null, OutputRetention.Complete, environment);
        Process process = await RecordAsync(command, ct);
        InputFeed? feed = input is null ? null : feeds.Register(connection.NookId, process.Id, input);
        ProcessRun run;
        try
        {
            bool sent = await connection.SendAsync(new DaemonInstruction(process.ToInstruction(environment, inputStreamed: feed is not null)), ct);
            if (!sent)
            {
                return new ProcessRun(ExitCode: ProcessExited.Lost, "The nook's daemon disconnected before the process could start; try again.");
            }

            run = await WatchToEndAsync(connection, process.Id, output, ct);
        }
        finally
        {
            if (feed is not null)
            {
                feeds.Forget(feed);
            }
        }

        if (feed is not null)
        {
            await feed.Finished;
        }

        return run;
    }

    // Relays the daemon's uploads until one ends with the exit, which is recorded first, so whoever
    // learns of an exit here finds it recorded, as the daemon's own report may not be yet. An upload
    // that breaks off, because the daemon's connection ended, is asked for again from where it stopped
    // on the next connection, so the watcher sees every byte once; on an instance that handed over,
    // the watch ends instead, and the watcher resumes on the next.
    public async IAsyncEnumerable<ProcessEvent> WatchAsync(DaemonConnection connection, ProcessId processId, long offset, [EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            using (OutputReceiver receiver = daemons.Expect(connection, processId))
            {
                try
                {
                    bool asked = await connection.SendAsync(new DaemonInstruction(new WatchOutputInstruction(receiver.WatchId, processId, offset)), ct);
                    if (asked)
                    {
                        await foreach (ProcessEvent processEvent in receiver.Events.ReadAllAsync(ct))
                        {
                            if (processEvent.Value is ProcessExited exited)
                            {
                                await RecordExitAsync(connection.NookId, exited, ct);
                            }

                            yield return processEvent;
                            if (processEvent.Value is not ProcessOutput output)
                            {
                                yield break;
                            }

                            offset = output.Offset + output.Data.Length;
                        }
                    }
                }
                finally
                {
                    daemons.Forget(receiver);
                }
            }

            DaemonConnection? reconnected = await daemons.WaitAsync(connection.NookId, DaemonConnections.Patience, ct);
            if (reconnected is null && daemons.Left)
            {
                yield break;
            }

            if (reconnected is null)
            {
                throw new InvalidOperationException("Nook " + connection.NookId.Value + "'s daemon didn't reconnect in time; watch the process again later.");
            }

            connection = reconnected;
        }
    }

    // Records a process's exit, from the daemon's report or a watch, whichever comes first.
    public async Task RecordExitAsync(NookId nookId, ProcessExited exited, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        Process? process = await db.Processes.SingleOrDefaultAsync(found => found.Id == exited.ProcessId && found.NookId == nookId, ct);
        process?.Exited(exited.ExitCode, time);

        // A conflict means the other one recorded it.
        await db.SaveAsync(ct);
    }

    // Ends every process the nook still has running, whose exit nobody will see: its sandbox stopped
    // without its memory, or is gone.
    public async Task EndAllAsync(NooksDbContext db, NookId nookId, CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        await db.Processes.Where(process => process.NookId == nookId && process.ExitCode == null)
            .ExecuteUpdateAsync(set => set.SetProperty(process => process.ExitCode, ProcessExited.Lost).SetProperty(process => process.ExitedAt, now), ct);
    }

    private async Task<ProcessRun> WatchToEndAsync(DaemonConnection connection, ProcessId processId, ScriptOutput? output, CancellationToken ct)
    {
        StringBuilder errors = new StringBuilder();
        int exitCode = ProcessExited.Lost;
        long written = 0;
        await foreach (ProcessEvent processEvent in WatchAsync(connection, processId, 0, ct))
        {
            if (processEvent.Value is ProcessOutput { Channel: OutputChannel.StandardOutput } printed && output is not null)
            {
                written += printed.Data.Length;
                if (written > output.Limit)
                {
                    _ = await connection.SendAsync(new DaemonInstruction(new StopProcessInstruction(processId)), ct);
                    string limit = string.Create(CultureInfo.InvariantCulture, $"{output.Limit / (1024 * 1024)} MiB");
                    return new ProcessRun(ExitCode: ProcessExited.Lost, "It came to more than the " + limit + " the control plane takes from it.");
                }

                await output.Destination.WriteAsync(printed.Data, ct);
            }
            else if (processEvent.Value is ProcessOutput { Channel: OutputChannel.StandardError } complained)
            {
                errors.Append(Encoding.UTF8.GetString(complained.Data.Span));
                errors.Remove(0, Math.Max(0, errors.Length - MaxKeptErrors));
            }
            else if (processEvent.Value is ProcessExited exited)
            {
                exitCode = exited.ExitCode;
            }
        }

        return new ProcessRun(exitCode, errors.ToString().Trim());
    }
}
