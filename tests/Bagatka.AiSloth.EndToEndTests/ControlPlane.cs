using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Bagatka.AiSloth.Cli;
using Bagatka.Sandboxing;
using Bagatka.Sandboxing.Docker;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

[assembly: AssemblyFixture(typeof(Bagatka.AiSloth.EndToEndTests.ControlPlane))]

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// The real app, started by the AppHost through Aspire's test builder: PostgreSQL, the migrations,
/// the nook image, and the WebApi, which accepts tokens from a fake identity provider in this process.
/// Tests call the public API over HTTP, as any client would.
/// </summary>
public sealed class ControlPlane : IAsyncLifetime
{
    private readonly int _daemonPort = FreePort();
    private FakeIssuer? _issuer;
    private DistributedApplication? _app;

    /// <summary>What <c>sloth machine connect</c> takes: the endpoint daemons and machines dial.</summary>
    public Uri MachinesUrl => new Uri(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{_daemonPort}"));

    // This run's nooks live in Docker scopes of their own, away from a developer's: one for the
    // docker provider, one for every machine the tests run.
    private string Scope { get; } = "e2e-" + RandomNumberGenerator.GetHexString(12, lowercase: true);

    private string MachineScope => Scope + "-m";

    private DistributedApplication App => _app ?? throw new InvalidOperationException("The app hasn't started.");

    private FakeIssuer Issuer => _issuer ?? throw new InvalidOperationException("The issuer hasn't started.");

    public async ValueTask InitializeAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _issuer = await FakeIssuer.StartAsync();
        IDistributedApplicationTestingBuilder appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Bagatka_AiSloth_AppHost>(
            [
                "Parameters:authentication-issuer=" + _issuer.Issuer,
                "Parameters:authentication-audience=" + FakeIssuer.Audience,
                "Parameters:sandbox-scope=" + Scope,
                "DaemonPort=" + _daemonPort.ToString(CultureInfo.InvariantCulture),
            ],
            ct);
        _app = await appHost.BuildAsync(ct);
        await _app.StartAsync(ct);
        await _app.ResourceNotifications.WaitForResourceHealthyAsync("webapi", ct);
    }

    /// <summary>
    /// A client signed in as whoever the identity provider knows by <paramref name="subject"/>; the
    /// WebApi records that user on their first call.
    /// </summary>
    public HttpClient ClientFor(string subject)
    {
        return ClientWithToken(Issuer.TokenFor(subject));
    }

    /// <summary>A client presenting <paramref name="token"/>, or no token when it is <see langword="null"/>.</summary>
    public HttpClient ClientWithToken(string? token)
    {
        HttpClient client = App.CreateHttpClient("webapi", "Http");
        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>Runs machine mode in this process, as <c>sloth machine run</c> would.</summary>
    internal RunningMachine StartMachine(MachineCredential credential)
    {
        return new RunningMachine(credential, new DockerSandboxSettings(new Uri("unix:///var/run/docker.sock"), MachineScope));
    }

    public async ValueTask DisposeAsync()
    {
        // The app goes first: while it runs, its reconciler would recreate the sandbox of any nook still being created.
        if (_app is not null)
        {
            await _app.DisposeAsync();
            await DeleteSandboxesAsync(Scope);
            await DeleteSandboxesAsync(MachineScope);
        }

        if (_issuer is not null)
        {
            await _issuer.DisposeAsync();
        }
    }

    // The daemon endpoint gets a port of its own, so tests run while the app runs for development.
    private static int FreePort()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    // Whatever tests left behind, including after a failure.
    private static async Task DeleteSandboxesAsync(string scope)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddDockerSandboxProvider(new DockerSandboxSettings(new Uri("unix:///var/run/docker.sock"), scope));
        await using ServiceProvider provider = services.BuildServiceProvider();
        ISandboxProvider docker = provider.GetRequiredService<ISandboxProvider>();
        await foreach (SandboxObservation sandbox in docker.ListAsync(CancellationToken.None))
        {
            await docker.DeleteAsync(sandbox.Key, CancellationToken.None);
        }
    }
}
