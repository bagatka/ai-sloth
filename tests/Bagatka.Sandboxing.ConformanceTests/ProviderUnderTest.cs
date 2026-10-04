using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Sandboxing.ConformanceTests;

/// <summary>
/// One provider in a scope of its own. Disposing it deletes everything left in the scope, even when
/// the test failed.
/// </summary>
internal sealed class ProviderUnderTest : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly RemoteLoop? _remote;

    /// <summary>Takes the provider from <paramref name="services"/>, or reaches it through a remote loop.</summary>
    public ProviderUnderTest(ServiceProvider services, bool throughRemote)
    {
        _services = services;
        ISandboxProvider local = services.GetRequiredService<ISandboxProvider>();
        _remote = throughRemote ? new RemoteLoop(local) : null;

        // Cleanup takes the same path the test did.
        Provider = _remote?.Provider ?? local;
    }

    public ISandboxProvider Provider { get; }

    /// <summary>The provider's services, for tests of one provider's own behavior.</summary>
    public IServiceProvider Services => _services;

    public async ValueTask DisposeAsync()
    {
        using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await foreach (SandboxObservation sandbox in Provider.ListAsync(timeout.Token))
        {
            await Provider.DeleteAsync(sandbox.Key, timeout.Token);
        }

        await foreach (SnapshotObservation snapshot in Provider.ListSnapshotsAsync(timeout.Token))
        {
            await Provider.DeleteSnapshotAsync(snapshot.Key, timeout.Token);
        }

        if (_remote is not null)
        {
            await _remote.DisposeAsync();
        }

        await _services.DisposeAsync();
    }
}
