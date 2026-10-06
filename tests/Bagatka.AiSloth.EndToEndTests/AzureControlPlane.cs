using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure.Identity;
using Bagatka.Sandboxing;
using Bagatka.Sandboxing.Azure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// An app whose nooks run on Azure and sleep as the sleepy app's do, started only when
/// BAGATKA_AZURE_SANDBOXES_GROUP is <c>subscription/resource-group/group/region</c> (with <c>az login</c>)
/// and BAGATKA_NGROK_ENV_FILE is an env file with <c>NGROK_AUTHTOKEN</c>. Nooks reach the app through
/// ngrok and pull its images from ttl.sh.
/// </summary>
public sealed class AzureControlPlane : IAsyncLifetime
{
    private static readonly string? Group = Environment.GetEnvironmentVariable("BAGATKA_AZURE_SANDBOXES_GROUP") is { Length: > 0 } group ? group : null;
    private static readonly string? TokenFile = Environment.GetEnvironmentVariable("BAGATKA_NGROK_ENV_FILE") is { Length: > 0 } file ? file : null;

    private NgrokTunnels? _tunnels;

    /// <summary>The app, or <see langword="null"/> when the Azure tests weren't asked for.</summary>
    public ControlPlane? ControlPlane { get; private set; }

    public async ValueTask InitializeAsync()
    {
        if (Group is null || TokenFile is null)
        {
            return;
        }

        CancellationToken ct = TestContext.Current.CancellationToken;
        int daemonPort = ControlPlane.FreePort();
        int modelsPort = ControlPlane.FreePort();
        _tunnels = await NgrokTunnels.StartAsync(TokenFile, daemonPort, modelsPort, ct);
        string repository = "ttl.sh/aisloth-e2e-" + RandomNumberGenerator.GetHexString(12, lowercase: true);
        ControlPlane = new ControlPlane(
            [
                "Parameters:nook-sleep-after=00:00:08",
                "Parameters:nook-evict-after=00:00:40",
                "Parameters:azure-sandbox-group=" + Group,
                "Parameters:nook-image-repository=" + repository,
                "Parameters:nook-daemon-url=" + _tunnels.DaemonUrl,
                "Parameters:nook-models-url=" + _tunnels.ModelsUrl,
            ],
            daemonPort,
            modelsPort);
        await ControlPlane.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (ControlPlane is not null)
        {
            await ControlPlane.DisposeAsync();
            await DeleteSandboxesAsync(ControlPlane.Scope);
        }

        if (_tunnels is not null)
        {
            await _tunnels.DisposeAsync();
        }
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
