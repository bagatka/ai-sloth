using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Daemon;

/// <summary>
/// The nook's disk as the daemon sees it: how full it is, and a reserve file that keeps a full disk
/// recoverable (<c>src/Daemon/README.md</c>, "Full disk").
/// </summary>
internal sealed class NookDisk(DaemonSettings settings, ILogger<NookDisk> logger)
{
    /// <summary>The reserve every nook keeps; tests use smaller ones.</summary>
    public const long DefaultReserveBytes = 256L << 20;

    private readonly Lock _gate = new Lock();
    private readonly string _reservePath = Path.Combine(settings.StateDirectory, "reserve");
    private TaskCompletionSource _full = NewSignal();

    /// <summary>Completes when the disk fills up, so the link reports it at once.</summary>
    public Task Full
    {
        get
        {
            lock (_gate)
            {
                return _full.Task;
            }
        }
    }

    /// <summary>How full the working directory's disk is.</summary>
    public DriveUsage Measure()
    {
        DriveInfo drive = new DriveInfo(settings.WorkingDirectory);
        return new DriveUsage(drive.TotalSize, drive.AvailableFreeSpace);
    }

    /// <summary>
    /// Takes the reserve, unless the daemon holds it already or the disk can't spare twice its size,
    /// so taking it never fills the disk.
    /// </summary>
    public void Reserve()
    {
        if (settings.DiskReserveBytes == 0 || File.Exists(_reservePath) || Measure().AvailableBytes < 2 * settings.DiskReserveBytes)
        {
            return;
        }

        Directory.CreateDirectory(settings.StateDirectory);
        try
        {
            // Preallocated, so the space is really taken rather than promised.
            FileStreamOptions options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, PreallocationSize = settings.DiskReserveBytes };
            using FileStream reserve = new FileStream(_reservePath, options);
        }
        catch (IOException exception)
        {
            File.Delete(_reservePath);
            Log.ReserveUnavailable(logger, exception);
        }
    }

    /// <summary>
    /// Output couldn't be written for lack of space: releases the reserve, so output and the commands
    /// that free space can write again, and reports the full disk.
    /// </summary>
    public void Filled()
    {
        if (!File.Exists(_reservePath))
        {
            return;
        }

        File.Delete(_reservePath);
        Log.ReserveReleased(logger);
        lock (_gate)
        {
            TaskCompletionSource full = _full;
            _full = NewSignal();
            full.TrySetResult();
        }
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
