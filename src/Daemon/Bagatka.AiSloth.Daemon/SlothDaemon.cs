using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Daemon;

/// <summary>
/// The daemon: its processes and its link to the control plane. Disposing it kills every process
/// that still runs, which is what happens when the nook stops.
/// </summary>
internal sealed class SlothDaemon : IAsyncDisposable
{
    private readonly NookDisk _disk;
    private readonly ProcessTable _processes;
    private readonly ControlPlaneLink _link;

    public SlothDaemon(DaemonSettings settings, TimeProvider time, ILoggerFactory loggers)
    {
        // Processes from an earlier run of the daemon ended with it, so their output is meaningless.
        string processes = Path.Combine(settings.StateDirectory, "processes");
        if (Directory.Exists(processes))
        {
            Directory.Delete(processes, recursive: true);
        }

        _disk = new NookDisk(settings, loggers.CreateLogger<NookDisk>());
        _processes = new ProcessTable(settings, _disk, loggers.CreateLogger<ProcessTable>());
        _link = new ControlPlaneLink(settings, _processes, _disk, new NookMeter(time), time, loggers.CreateLogger<ControlPlaneLink>());
    }

    /// <summary>Serves the control plane until <paramref name="ct"/> is cancelled.</summary>
    public Task RunAsync(CancellationToken ct)
    {
        _disk.Reserve();
        return _link.RunAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        _link.Dispose();
        await _processes.DisposeAsync();
    }
}
