using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    // How much of what a run prints to standard error is kept: enough to say why it failed.
    private const int MaxKeptErrors = 4000;

    // The most a run may write out: archives and bundles land on the control plane's disk first.
    private const long MaxOutputBytes = 4L * 1024 * 1024 * 1024;

    private static readonly IReadOnlyDictionary<string, string> NoVariables = new Dictionary<string, string>(StringComparer.Ordinal);

    // Runs a shell script the control plane needs in a nook, to its end: copying files in and out. It
    // is recorded like any process, so people see what ran in their nook, in a database context of its
    // own, so runs can feed each other. Its standard input comes from `input`, which the daemon reads
    // on its own stream; its standard output goes to `output`. A writer of input that fails throws here.
    private async Task<ProcessRun> RunAsync(
        DaemonConnection connection,
        string script,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string> environment,
        Func<Stream, CancellationToken, Task>? input,
        Stream? output,
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
                return new ProcessRun(ExitCode: -1, "The nook's daemon disconnected before the process could start; try again.");
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

    // Records a process the control plane runs, in a database context of its own, before it starts.
    private async Task<Process> RecordAsync(StartProcess command, CancellationToken ct)
    {
        Result<Process> started = Process.Start(command, time);
        if (started.Failed)
        {
            throw new InvalidOperationException("A script the control plane runs in nooks is invalid: " + started.Error.Message);
        }

        await using NooksDbContext recording = await databases.CreateDbContextAsync(ct);
        recording.Processes.Add(started.Output);
        Result saved = await recording.SaveAsync(ct);
        if (saved.Failed)
        {
            throw new InvalidOperationException("Recording a process the control plane runs failed: " + saved.Error.Message);
        }

        return started.Output;
    }

    private async Task<ProcessRun> WatchToEndAsync(DaemonConnection connection, ProcessId processId, Stream? output, CancellationToken ct)
    {
        StringBuilder errors = new StringBuilder();
        int exitCode = -1;
        long written = 0;
        await foreach (ProcessEvent processEvent in WatchAsync(connection, processId, 0, ct))
        {
            if (processEvent.Value is ProcessOutput { Channel: OutputChannel.StandardOutput } printed && output is not null)
            {
                written += printed.Data.Length;
                if (written > MaxOutputBytes)
                {
                    _ = await connection.SendAsync(new DaemonInstruction(new StopProcessInstruction(processId)), ct);
                    return new ProcessRun(ExitCode: -1, "It came to more than the 4 GiB the control plane takes at once.");
                }

                await output.WriteAsync(printed.Data, ct);
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

    // How a run ended: its exit code, and the end of what it printed to standard error.
    private sealed record ProcessRun(int ExitCode, string Errors)
    {
        public bool Succeeded => ExitCode == 0;
    }
}
