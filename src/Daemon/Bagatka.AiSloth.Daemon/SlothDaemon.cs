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
    private readonly ProcessTable _processes;
    private readonly ControlPlaneLink _link;
    private readonly TimeProvider _time;

    public SlothDaemon(DaemonSettings settings, TimeProvider time, ILoggerFactory loggers)
    {
        // Processes from an earlier run of the daemon ended with it, so their output is meaningless.
        string processes = Path.Combine(settings.StateDirectory, "processes");
        if (Directory.Exists(processes))
        {
            Directory.Delete(processes, recursive: true);
        }

        _time = time;
        _processes = new ProcessTable(settings, loggers.CreateLogger<ProcessTable>());
        _link = new ControlPlaneLink(settings, _processes, loggers.CreateLogger<ControlPlaneLink>());
    }

    /// <summary>Serves the control plane until <paramref name="ct"/> is cancelled.</summary>
    public Task RunAsync(CancellationToken ct)
    {
        return _link.RunAsync(_time, ct);
    }

    public async ValueTask DisposeAsync()
    {
        _link.Dispose();
        await _processes.DisposeAsync();
    }
}
