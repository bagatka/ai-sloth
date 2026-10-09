using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Bagatka.Sandboxing;
using Bagatka.Sandboxing.Azure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: AssemblyFixture(typeof(Bagatka.AiSloth.EndToEndTests.AzureControlPlane))]

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// An app whose nooks run on Azure and sleep as the sleepy app's do, started by the first test that
/// asks, and only when BAGATKA_AZURE_SANDBOXES_GROUP is <c>subscription/resource-group/group/region</c>
/// (with <c>az login</c>), BAGATKA_NGROK_ENV_FILE is an env file with <c>NGROK_AUTHTOKEN</c>, and
/// BAGATKA_NOOK_IMAGE_REPOSITORY is a public repository this computer's Docker can push to, such as
/// <c>ghcr.io/bagatka</c>. Nooks reach the app through ngrok and pull its images, tagged <c>e2e</c>,
/// from that repository; a run pushes only the layers it doesn't have yet.
/// </summary>
public sealed class AzureControlPlane : IAsyncDisposable
{
    /// <summary>How long tests wait for a first turn, which waits for Azure to read an image it hasn't seen; for sleep or waking; and for eviction.</summary>
    public static readonly TimeSpan FirstTurn = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan Sleep = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan Eviction = TimeSpan.FromMinutes(3);

    private static readonly string? Group = Environment.GetEnvironmentVariable("BAGATKA_AZURE_SANDBOXES_GROUP") is { Length: > 0 } group ? group : null;
    private static readonly string? TokenFile = Environment.GetEnvironmentVariable("BAGATKA_NGROK_ENV_FILE") is { Length: > 0 } file ? file : null;
    private static readonly string? ImageRepository = Environment.GetEnvironmentVariable("BAGATKA_NOOK_IMAGE_REPOSITORY") is { Length: > 0 } repository ? repository : null;

    private readonly Lazy<Task<ControlPlane?>> _started;
    private NgrokTunnels? _tunnels;
    private ControlPlane? _app;

    public AzureControlPlane()
    {
        _started = new Lazy<Task<ControlPlane?>>(StartAsync);
    }

    /// <summary>The app, started by the first test that asks; <see langword="null"/> when the Azure tests weren't asked for.</summary>
    public Task<ControlPlane?> StartedAsync()
    {
        return _started.Value;
    }

    /// <summary>Waits until a nook's sandbox is gone from Azure, such as after a long sleep; false when it stays.</summary>
    public static async Task<bool> SandboxGoneAsync(ControlPlane app, Guid nookId, TimeSpan patience)
    {
        string[] group = Group!.Split('/');
        ServiceCollection services = new ServiceCollection();
        services.AddAzureSandboxProvider(new AzureSandboxSettings(group[0], group[1], group[2], group[3], app.Scope), new AzureCliCredential());
        await using ServiceProvider provider = services.BuildServiceProvider();
        ISandboxProvider azure = provider.GetRequiredService<ISandboxProvider>();
        long started = TimeProvider.System.GetTimestamp();
        SandboxObservation? sandbox = await azure.ObserveAsync(SandboxKey.From(nookId), CancellationToken.None);
        while (sandbox is not null && TimeProvider.System.GetElapsedTime(started) < patience)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
            sandbox = await azure.ObserveAsync(SandboxKey.From(nookId), CancellationToken.None);
        }

        return sandbox is null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync();
            await DeleteSandboxesAsync(_app.Scope);
        }

        if (_tunnels is not null)
        {
            await _tunnels.DisposeAsync();
        }
    }

    private async Task<ControlPlane?> StartAsync()
    {
        if (Group is null || TokenFile is null || ImageRepository is null)
        {
            return null;
        }

        CancellationToken ct = TestContext.Current.CancellationToken;
        int daemonPort = ControlPlane.FreePort();
        int modelsPort = ControlPlane.FreePort();
        _tunnels = await NgrokTunnels.StartAsync(TokenFile, daemonPort, modelsPort, ct);
        _app = new ControlPlane(
            [
                "Parameters:nook-harnesses=claude-code",
                "Parameters:azure-sandbox-group=" + Group,
                "Parameters:nook-image-repository=" + ImageRepository,
                "Parameters:nook-image-tag=e2e",
                "Parameters:nook-daemon-url=" + _tunnels.DaemonUrl,
                "Parameters:nook-models-url=" + _tunnels.ModelsUrl,
            ],
            [("Modules__Nooks__SleepAfter", "00:00:08"), ("Modules__Nooks__EvictAfter", "00:00:20")],
            daemonPort,
            modelsPort);
        await _app.InitializeAsync();
        return _app;
    }

    // Whatever the run left on Azure, after the app stopped, so its reconciler can't create them again.
    private static async Task DeleteSandboxesAsync(string scope)
    {
        string[] group = Group!.Split('/');
        ServiceCollection services = new ServiceCollection();
        services.AddAzureSandboxProvider(new AzureSandboxSettings(group[0], group[1], group[2], group[3], scope), new AzureCliCredential());
        await using ServiceProvider provider = services.BuildServiceProvider();
        ISandboxProvider azure = provider.GetRequiredService<ISandboxProvider>();
        await foreach (SandboxObservation sandbox in azure.ListAsync(CancellationToken.None))
        {
            await azure.DeleteAsync(sandbox.Key, CancellationToken.None);
        }

        await foreach (SnapshotObservation snapshot in azure.ListSnapshotsAsync(CancellationToken.None))
        {
            await azure.DeleteSnapshotAsync(snapshot.Key, CancellationToken.None);
        }
    }
}
