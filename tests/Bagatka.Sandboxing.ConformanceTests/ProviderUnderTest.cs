using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Sandboxing.ConformanceTests;

/// <summary>
/// One provider in a scope of its own. Disposing it deletes everything left in the scope, even when
/// the test failed.
/// </summary>
internal sealed class ProviderUnderTest(ServiceProvider services) : IAsyncDisposable
{
    public ISandboxProvider Provider { get; } = services.GetRequiredService<ISandboxProvider>();

    /// <summary>The provider's services, for tests of one provider's own behavior.</summary>
    public IServiceProvider Services => services;

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

        await services.DisposeAsync();
    }
}
