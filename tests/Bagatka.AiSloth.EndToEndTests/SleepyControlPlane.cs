using System.Threading.Tasks;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A second app whose nooks fall asleep after 8 idle seconds and are evicted after 40 asleep, for the
/// tests of sleep; the other tests' app keeps the defaults, so their nooks never sleep halfway.
/// </summary>
public sealed class SleepyControlPlane : IAsyncLifetime
{
    public ControlPlane ControlPlane { get; } = new ControlPlane(["Parameters:nook-sleep-after=00:00:08", "Parameters:nook-evict-after=00:00:40"]);

    public async ValueTask InitializeAsync()
    {
        await ControlPlane.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await ControlPlane.DisposeAsync();
    }
}
