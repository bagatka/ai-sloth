using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
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
using Aspire.Hosting.ApplicationModel;
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
public sealed partial class ControlPlane : IAsyncLifetime
{
    private readonly int _daemonPort = FreePort();
    private readonly int _modelsPort = FreePort();
    private FakeIssuer? _issuer;
    private FakeModel? _model;
    private FakeChatGpt? _chatGpt;
    private FakeGitHub? _gitHub;
    private DistributedApplication? _app;

    /// <summary>What <c>sloth machine connect</c> takes: the endpoint daemons and machines dial.</summary>
    public Uri MachinesUrl => new Uri(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{_daemonPort}"));

    // This run's nooks live in Docker scopes of their own, away from a developer's: one for the
    // docker provider, one for every machine the tests run.
    private string Scope { get; } = "e2e-" + RandomNumberGenerator.GetHexString(12, lowercase: true);

    private string MachineScope => Scope + "-m";

    /// <summary>The model agents talk to through the gateway.</summary>
    internal FakeModel Model
    {
        get
        {
            if (_model is null)
            {
                throw new InvalidOperationException("The model hasn't started.");
            }

            return _model;
        }
    }

    /// <summary>Sign in with ChatGPT, and the server ChatGPT plans' calls go to (<see cref="Model"/>).</summary>
    internal FakeChatGpt ChatGpt
    {
        get
        {
            if (_chatGpt is null)
            {
                throw new InvalidOperationException("ChatGPT hasn't started.");
            }

            return _chatGpt;
        }
    }

    /// <summary>The WebApi's public endpoint: what <c>sloth host add</c> takes.</summary>
    public Uri WebApiUrl => App.GetEndpoint("webapi", "Http");

    /// <summary>GitHub, which people connect and whose repositories nooks start with.</summary>
    internal FakeGitHub GitHub
    {
        get
        {
            if (_gitHub is null)
            {
                throw new InvalidOperationException("GitHub hasn't started.");
            }

            return _gitHub;
        }
    }

    /// <summary>The model gateway on the endpoint agents in nooks reach.</summary>
    public Uri ModelGatewayUrl => new Uri(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{_modelsPort}/models/"));

    private DistributedApplication App
    {
        get
        {
            if (_app is null)
            {
                throw new InvalidOperationException("The app hasn't started.");
            }

            return _app;
        }
    }

    private FakeIssuer Issuer
    {
        get
        {
            if (_issuer is null)
            {
                throw new InvalidOperationException("The issuer hasn't started.");
            }

            return _issuer;
        }
    }

    public async ValueTask InitializeAsync()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        _issuer = await FakeIssuer.StartAsync();
        _model = await FakeModel.StartAsync();
        _chatGpt = await FakeChatGpt.StartAsync();
        _gitHub = await FakeGitHub.StartAsync();
        IDistributedApplicationTestingBuilder appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Bagatka_AiSloth_AppHost>(
            [
                "Parameters:sign-in-provider-issuer=" + _issuer.Issuer,
                "Parameters:sign-in-provider-client-id=" + FakeIssuer.ClientId,
                "Parameters:sign-in-provider-client-secret=" + FakeIssuer.ClientSecret,
                "Parameters:sign-in-provider-name=Fake",
                "Parameters:invite-sign-up=true",
                "Parameters:sandbox-scope=" + Scope,
                "Parameters:model-private-networks=true",
                "Parameters:allow-chatgpt-plans=true",
                "Parameters:chatgpt-authority=" + _chatGpt.Url,
                "Parameters:chatgpt-api=" + _model.OpenAIUrl,
                "Parameters:github-app-client-id=" + FakeGitHub.ClientId,
                "Parameters:github-app-client-secret=" + FakeGitHub.ClientSecret,
                "Parameters:github-app-slug=" + FakeGitHub.AppSlug,
                "Parameters:github-api=" + _gitHub.ApiUrl,
                "Parameters:github-web=" + _gitHub.Url,
                "Parameters:sources-key=" + RandomNumberGenerator.GetHexString(64),
                "Parameters:agent-accounts-key=" + RandomNumberGenerator.GetHexString(64),
                "Parameters:secrets-key=" + RandomNumberGenerator.GetHexString(64),
                "DaemonPort=" + _daemonPort.ToString(CultureInfo.InvariantCulture),
                "ModelsPort=" + _modelsPort.ToString(CultureInfo.InvariantCulture),
            ],
            ct);
        _app = await appHost.BuildAsync(ct);

        await _app.StartAsync(ct);
        ResourceEvent webApi = await _app.ResourceNotifications.WaitForResourceHealthyAsync("webapi", ct);

        // Nobody has signed up yet, so the WebApi printed the host's setup code as it started; its
        // first person takes it before any test runs.
        SetupCode = await ReadSetupCodeAsync(webApi.ResourceId, ct);
        using HttpClient anonymous = ClientWithToken(token: null);
        Owner = await Api.ReadAsync<SignInEndpointsShapes.SignedIn>(
            anonymous.SendPostAsync("/sign-in/code", new { code = SetupCode, name = "Owner", device = "e2e" }), HttpStatusCode.OK);
    }

    /// <summary>The setup code the host printed when nobody had signed up; its first person used it.</summary>
    public string SetupCode { get; private set; } = string.Empty;

    /// <summary>The host's first person, signed in with the setup code.</summary>
    internal SignInEndpointsShapes.SignedIn? Owner { get; private set; }

    /// <summary>
    /// A client signed in as whoever the identity provider knows by <paramref name="subject"/>: on its
    /// first call it signs in through the host's provider, as a person's browser and the CLI would, and
    /// someone new gets a workspace of their own.
    /// </summary>
    public HttpClient ClientFor(string subject)
    {
        ProviderSignIn signIn = new ProviderSignIn(WebApiUrl, subject) { InnerHandler = new SocketsHttpHandler() };
        return new HttpClient(signIn) { BaseAddress = WebApiUrl };
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

        if (_model is not null)
        {
            await _model.DisposeAsync();
        }

        if (_chatGpt is not null)
        {
            await _chatGpt.DisposeAsync();
        }

        if (_gitHub is not null)
        {
            await _gitHub.DisposeAsync();
        }
    }

    // The line the WebApi prints to its console: "First sign-in: sloth host add <url> --code <code> ...".
    // Read from its running instance's logs, which keep what came before watching.
    private async Task<string> ReadSetupCodeAsync(string instance, CancellationToken ct)
    {
        using CancellationTokenSource patience = CancellationTokenSource.CreateLinkedTokenSource(ct);
        patience.CancelAfter(TimeSpan.FromMinutes(5));
        ResourceLoggerService logs = App.Services.GetRequiredService<ResourceLoggerService>();
        await foreach (IReadOnlyList<LogLine> batch in logs.WatchAsync(instance).WithCancellation(patience.Token))
        {
            foreach (LogLine line in batch)
            {
                Match found = SetupLine().Match(line.Content);
                if (found.Success)
                {
                    return found.Groups["code"].Value;
                }
            }
        }

        throw new InvalidOperationException("The WebApi printed no setup code.");
    }

    [GeneratedRegex("First sign-in: sloth host add [^ ]+ --code (?<code>[A-Z0-9]{16}) ", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SetupLine();

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
