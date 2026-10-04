using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.AiSloth.DaemonProtocol.V1;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Daemon;

/// <summary>
/// The processes this daemon started. Running processes stay until they exit; exited ones stay, with
/// their output, until more than <see cref="MaxExitedProcesses"/> have exited, oldest first.
/// </summary>
internal sealed class ProcessTable(DaemonSettings settings, ILogger<ProcessTable> logger) : IAsyncDisposable
{
    private const int MaxExitedProcesses = 100;

    private readonly Lock _gate = new Lock();
    private readonly List<NookProcess> _processes = [];

    // Exits are re-sent on every connection anyway, so the oldest may be dropped while disconnected.
    private readonly Channel<ProcessExited> _exits = Channel.CreateBounded<ProcessExited>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });

    // Names output directories, so the control plane's process IDs stay opaque to the daemon.
    private int _started;

    /// <summary>Exits as they happen.</summary>
    public ChannelReader<ProcessExited> Exits => _exits.Reader;

    /// <summary>Starts the instructed process. A repeated instruction for the same process ID is ignored.</summary>
    public async Task StartAsync(StartProcess instruction)
    {
        OutputRetention retention = instruction.Retention switch
        {
            OutputRetention.Complete => OutputRetention.Complete,

            // Unspecified is what an older control plane sends: the protocol's default retention.
            OutputRetention.Recent or OutputRetention.Unspecified => OutputRetention.Recent,
        };

        List<NookProcess> evicted;
        lock (_gate)
        {
            if (_processes.Exists(process => string.Equals(process.Id, instruction.ProcessId, StringComparison.Ordinal)))
            {
                return;
            }

            _started++;
            string directory = Path.Combine(settings.StateDirectory, "processes", _started.ToString(CultureInfo.InvariantCulture));
            OutputJournal output = new OutputJournal(directory, retention, settings.Limits);
            _processes.Add(NookProcess.Start(instruction, settings.WorkingDirectory, output, settings.Limits.ChunkBytes, _exits.Writer));
            evicted = Evict();
        }

        Log.ProcessStarted(logger, instruction.ProcessId);
        foreach (NookProcess process in evicted)
        {
            // Evicted processes have exited, so disposing only deletes their output.
            await process.DisposeAsync();
        }
    }

    /// <summary>Starts stopping a process; unknown and exited processes are ignored.</summary>
    public void Stop(string processId)
    {
        Find(processId)?.RequestStop();
    }

    /// <summary>Queues input for a process; unknown and exited processes are ignored.</summary>
    public async Task SendInputAsync(string processId, ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (Find(processId) is NookProcess process)
        {
            await process.SendInputAsync(data, ct);
        }
    }

    /// <summary>The process, or <see langword="null"/> when this daemon has no such process.</summary>
    public NookProcess? Find(string processId)
    {
        lock (_gate)
        {
            return _processes.Find(process => string.Equals(process.Id, processId, StringComparison.Ordinal));
        }
    }

    /// <summary>Processes still running, for the hello on a new connection.</summary>
    public IReadOnlyList<RunningProcess> Running()
    {
        lock (_gate)
        {
            return _processes
                .Where(process => !process.Exited.IsCompleted)
                .Select(process => new RunningProcess { ProcessId = process.Id, OutputLength = process.Output.Length })
                .ToList();
        }
    }

    /// <summary>Processes that have exited, to report again on a new connection.</summary>
    public IReadOnlyList<ProcessExited> Exited()
    {
        lock (_gate)
        {
            return _processes
                .Where(process => process.Exited.IsCompletedSuccessfully)
                .Select(process => new ProcessExited { ProcessId = process.Id, ExitCode = process.Exited.Result })
                .ToList();
        }
    }

    /// <summary>Kills every process that still runs and deletes all output.</summary>
    public async ValueTask DisposeAsync()
    {
        List<NookProcess> processes;
        lock (_gate)
        {
            processes = [.. _processes];
            _processes.Clear();
        }

        _exits.Writer.TryComplete();
        foreach (NookProcess process in processes)
        {
            await process.DisposeAsync();
        }
    }

    // Callers hold _gate.
    private List<NookProcess> Evict()
    {
        List<NookProcess> exited = _processes.Where(process => process.Exited.IsCompleted).ToList();
        List<NookProcess> evicted = exited.Take(Math.Max(0, exited.Count - MaxExitedProcesses)).ToList();
        foreach (NookProcess process in evicted)
        {
            _processes.Remove(process);
        }

        return evicted;
    }
}
