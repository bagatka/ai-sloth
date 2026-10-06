using System;
using System.Threading.Tasks;
using Xunit;

[assembly: AssemblyFixture(typeof(Bagatka.AiSloth.EndToEndTests.DeployedControlPlane))]

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// An app of its own, whose WebApi the deploy journey replaces in the middle of a turn, so no other
/// journey runs into the swap. It starts when that journey first asks for it.
/// </summary>
public sealed class DeployedControlPlane : IAsyncDisposable
{
    private readonly ControlPlane _app = new ControlPlane([]);
    private readonly Lazy<Task> _started;

    public DeployedControlPlane()
    {
        _started = new Lazy<Task>(() => _app.InitializeAsync().AsTask());
    }

    /// <summary>The app, started by the first test that asks.</summary>
    public async Task<ControlPlane> StartedAsync()
    {
        await _started.Value;
        return _app;
    }

    public async ValueTask DisposeAsync()
    {
        if (_started.IsValueCreated)
        {
            await _app.DisposeAsync();
        }
    }
}
