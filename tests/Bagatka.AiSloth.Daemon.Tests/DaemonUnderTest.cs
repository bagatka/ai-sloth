using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bagatka.AiSloth.Daemon.Tests;

/// <summary>
/// A real daemon, in this process, with its working and state directories in a temporary folder.
/// Disposing it stops the daemon, kills its processes, and deletes the folder.
/// </summary>
internal sealed class DaemonUnderTest : IAsyncDisposable
{
    public const string Token = "test-token";

    private readonly CancellationTokenSource _stop = new CancellationTokenSource();
    private readonly SlothDaemon _daemon;
    private readonly Task _running;
    private readonly string _root;

    private DaemonUnderTest(DaemonSettings settings, string root)
    {
        _root = root;
        _daemon = new SlothDaemon(settings, TimeProvider.System, NullLoggerFactory.Instance);
        _running = _daemon.RunAsync(_stop.Token);
    }

    public static Guid NookId { get; } = Guid.CreateVersion7();

    public static DaemonUnderTest Start(Uri controlPlane, OutputLimits? limits = null)
    {
        string root = Path.Combine(Path.GetTempPath(), "slothd-tests", RandomNumberGenerator.GetHexString(12, lowercase: true));
        string work = Path.Combine(root, "work");
        Directory.CreateDirectory(work);
        DaemonSettings settings = new DaemonSettings(controlPlane, NookId, Token, work, Path.Combine(root, "state"), limits ?? OutputLimits.Default);
        return new DaemonUnderTest(settings, root);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        try
        {
            await _running;
        }
        catch (OperationCanceledException)
        {
            // Stopping is what we asked for.
        }

        await _daemon.DisposeAsync();
        _stop.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
