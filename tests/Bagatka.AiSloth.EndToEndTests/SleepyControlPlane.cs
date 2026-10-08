using System;
using System.Threading.Tasks;
using Xunit;

[assembly: AssemblyFixture(typeof(Bagatka.AiSloth.EndToEndTests.SleepyControlPlane))]

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A second app whose nooks fall asleep after 8 idle seconds and are evicted after 20 asleep, whose
/// workspaces have at most two nooks awake, and whose drafts go after 30 seconds, for the tests of
/// nooks' lives; the other tests' app keeps the defaults,
/// so their nooks never sleep halfway. It starts when a test first asks for it, so runs without those
/// tests never wait for it, and every class that uses it runs at the same time as the others.
/// </summary>
public sealed class SleepyControlPlane : IAsyncDisposable
{
    /// <summary>
    /// How long tests wait for a nook to fall asleep or wake, and to be evicted. A chat keeps its nook
    /// awake for half a minute after its last work, before the 8 idle seconds start.
    /// </summary>
    public static readonly TimeSpan Sleep = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan Eviction = TimeSpan.FromMinutes(2);

    private readonly ControlPlane _app = new ControlPlane([], [("Modules__Nooks__SleepAfter", "00:00:08"), ("Modules__Nooks__EvictAfter", "00:00:20"), ("Modules__Nooks__MaxAwakePerWorkspace", "2"), ("Modules__Chats__DraftLifetime", "00:00:30")]);
    private readonly Lazy<Task> _started;

    public SleepyControlPlane()
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
