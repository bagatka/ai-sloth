using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;
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
using Bagatka.Foundation;
using Bagatka.Sandboxing;
using Bagatka.Sandboxing.Docker;
using Bagatka.Sdk.Docker;
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
    private readonly int _daemonPort;
    private readonly int _modelsPort;
    private FakeIssuer? _issuer;
    private FakeModel? _model;
    private FakeChatGpt? _chatGpt;
    private FakeGitHub? _gitHub;
    private DistributedApplication? _app;
    private ResourceLogFiles? _logs;

    /// <summary>What <c>sloth machine connect</c> takes: the endpoint daemons and machines dial.</summary>
    public Uri MachinesUrl => new Uri(string.Create(CultureInfo.InvariantCulture, $"http://localhost:{_daemonPort}"));

    // The Docker Engine nooks run in, which has Sysbox: DOCKER_HOST's, as for the docker command, or
    // the default one.
    private static Uri DockerEndpoint { get; } = new Uri(Environment.GetEnvironmentVariable("DOCKER_HOST") is { Length: > 0 } host ? host : "unix:///var/run/docker.sock");

    // This run's nooks live in scopes of their own, away from a developer's: one for the docker and
    // azure providers, one for every machine the tests run.
    internal string Scope { get; } = "e2e-" + RandomNumberGenerator.GetHexString(12, lowercase: true);

    private string MachineScope => Scope + "-m";

    // This run's checkpoints, deleted with it.
    private string ObjectStorage => Path.Combine(Path.GetTempPath(), "aisloth-" + Scope);

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
                "Parameters:object-storage=" + ObjectStorage,
                "Parameters:model-private-networks=true",
                "Parameters:allow-chatgpt-plans=true",
                "Parameters:github-app-client-id=" + FakeGitHub.ClientId,
                "Parameters:github-app-client-secret=" + FakeGitHub.ClientSecret,
                "Parameters:github-app-slug=" + FakeGitHub.AppSlug,
                "Parameters:encryption-key=" + RandomNumberGenerator.GetHexString(64),
                "DaemonPort=" + _daemonPort.ToString(CultureInfo.InvariantCulture),
                "ModelsPort=" + _modelsPort.ToString(CultureInfo.InvariantCulture),
                "DOCKER_HOST=" + DockerEndpoint,
                .. _settings,
            ],
            ct);

        // The app's own output and Aspire's warnings go to files, by this run's scope.
        ResourceLogFiles logs = new ResourceLogFiles(Path.Combine(AppContext.BaseDirectory, "TestResults", "logs", Scope));
        _logs = logs;
        appHost.Services.AddLogging(logs.Route);

        ConfigureWebApi(appHost.CreateResourceBuilder<ProjectResource>("webapi"));

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

    // The paid and external services are fakes in this process; no dashboard runs to send telemetry
    // to, and flushing it would hold every stop of the WebApi for seconds; every journey signs people
    // in from this computer's one address; and the rest of the settings are the test's own, such as
    // short sleep periods.
    private void ConfigureWebApi(IResourceBuilder<ProjectResource> webApi)
    {
        webApi.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", string.Empty)
            .WithEnvironment("Host__SignInsPerMinute", "100000")
            .WithEnvironment("Modules__AgentAccounts__ChatGptAuthority", ChatGpt.Url.AbsoluteUri)
            .WithEnvironment("Modules__AgentAccounts__ChatGptApi", Model.OpenAIUrl.AbsoluteUri)
            .WithEnvironment("GitHub__ApiUrl", GitHub.ApiUrl.AbsoluteUri)
            .WithEnvironment("GitHub__WebUrl", GitHub.Url.AbsoluteUri);
        foreach ((string name, string value) in _environment)
        {
            webApi.WithEnvironment(name, value);
        }
    }

    private readonly IReadOnlyList<string> _settings;
    private readonly IReadOnlyList<(string Name, string Value)> _environment;

    /// <summary>The app as it runs for most tests.</summary>
    public ControlPlane()
        : this([], [])
    {
    }

    /// <summary>
    /// The app with settings of its own, for tests that need them: AppHost arguments, and the WebApi's
    /// environment, such as <c>Modules__Nooks__SleepAfter</c>.
    /// </summary>
    internal ControlPlane(IReadOnlyList<string> settings, IReadOnlyList<(string Name, string Value)> environment)
        : this(settings, environment, FreePort(), FreePort())
    {
    }

    /// <summary>The app with settings of its own, its nook-facing endpoints on given ports, such as ports a tunnel forwards to.</summary>
    internal ControlPlane(IReadOnlyList<string> settings, IReadOnlyList<(string Name, string Value)> environment, int daemonPort, int modelsPort)
    {
        _settings = settings;
        _environment = environment;
        _daemonPort = daemonPort;
        _modelsPort = modelsPort;
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

    /// <summary>
    /// Removes a nook's container behind the app's back, on the docker provider or a machine, as when
    /// the computer it ran on is lost with its disk.
    /// </summary>
    internal async Task LoseSandboxAsync(Guid nookId)
    {
        foreach (string scope in new[] { Scope, MachineScope })
        {
            ServiceCollection services = new ServiceCollection();
            services.AddDockerSandboxProvider(new DockerSandboxSettings(DockerEndpoint, scope));
            await using ServiceProvider provider = services.BuildServiceProvider();
            await provider.GetRequiredService<ISandboxProvider>().DeleteAsync(SandboxKey.From(nookId), CancellationToken.None);
        }
    }

    /// <summary>
    /// Leaves a sandbox in the app's scope that no nook records, as a database reset would; returns
    /// its key.
    /// </summary>
    internal async Task<Guid> LeaveOrphanSandboxAsync()
    {
        Guid key = Guid.CreateVersion7();
        ServiceCollection services = new ServiceCollection();
        services.AddDockerSandboxProvider(new DockerSandboxSettings(DockerEndpoint, Scope));
        await using ServiceProvider provider = services.BuildServiceProvider();
        SandboxSpec spec = new SandboxSpec(SandboxKey.From(key), new SandboxSource(new SandboxImage("aisloth-nook:dev")), new SandboxResources(500, 256), new Dictionary<string, string>(StringComparer.Ordinal), Location: null);
        Result<SandboxObservation> created = await provider.GetRequiredService<ISandboxProvider>().CreateAsync(spec, TestContext.Current.CancellationToken);
        Assert.False(created.Failed, created.Failed ? created.Error.Message : null);
        return key;
    }

    /// <summary>Waits until a nook's container is gone, on the docker provider or a machine, such as after a long sleep; false when it stays.</summary>
    internal async Task<bool> SandboxGoneAsync(Guid nookId, TimeSpan patience)
    {
        long started = TimeProvider.System.GetTimestamp();
        bool exists = await SandboxExistsAsync(nookId);
        while (exists && TimeProvider.System.GetElapsedTime(started) < patience)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
            exists = await SandboxExistsAsync(nookId);
        }

        return !exists;
    }

    /// <summary>Whether a nook's container exists, on the docker provider or a machine.</summary>
    internal async Task<bool> SandboxExistsAsync(Guid nookId)
    {
        foreach (string scope in new[] { Scope, MachineScope })
        {
            ServiceCollection services = new ServiceCollection();
            services.AddDockerSandboxProvider(new DockerSandboxSettings(DockerEndpoint, scope));
            await using ServiceProvider provider = services.BuildServiceProvider();
            SandboxObservation? sandbox = await provider.GetRequiredService<ISandboxProvider>().ObserveAsync(SandboxKey.From(nookId), CancellationToken.None);
            if (sandbox is not null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Replaces the WebApi as a deploy does: the running one is told to stop, and once it has stopped
    /// a new one starts; returns when the new one is healthy. <paramref name="whileStopping"/> runs once
    /// the old one is stopping, while it finishes its requests in flight.
    /// </summary>
    internal async Task ReplaceWebApiAsync(Action whileStopping)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        Task stopping = App.ResourceNotifications.WaitForResourceAsync("webapi", KnownResourceStates.Stopping, ct);
        IResource webApi = App.Services.GetRequiredService<DistributedApplicationModel>().Resources.Single(resource => string.Equals(resource.Name, "webapi", StringComparison.Ordinal));
        Task<ExecuteCommandResult> restarting = App.ResourceCommands.ExecuteCommandAsync(webApi, KnownResourceCommands.RestartCommand, ct);
        await stopping;
        whileStopping();
        ExecuteCommandResult restarted = await restarting;
        Assert.True(restarted.Success, restarted.Message);
        await App.ResourceNotifications.WaitForResourceHealthyAsync("webapi", ct);
    }

    /// <summary>Runs machine mode in this process, as <c>sloth machine run</c> would.</summary>
    internal RunningMachine StartMachine(MachineCredential credential)
    {
        DockerSandboxSettings docker = new DockerSandboxSettings(DockerEndpoint, MachineScope) { HostPorts = [_daemonPort, _modelsPort] };
        return new RunningMachine(credential, docker);
    }

    public async ValueTask DisposeAsync()
    {
        // The app goes first: while it runs, its reconciler would recreate the sandbox of any nook still being created.
        List<string> leftBehind = [];
        if (_app is not null)
        {
            await _app.DisposeAsync();
            await DeleteSandboxesAsync(Scope);
            await DeleteSandboxesAsync(MachineScope);
            List<string> app = await RemoveLeftBehindAsync(Scope);
            List<string> machine = await RemoveLeftBehindAsync(MachineScope);
            leftBehind = [.. app, .. machine];
            if (Directory.Exists(ObjectStorage))
            {
                Directory.Delete(ObjectStorage, recursive: true);
            }
        }

        _logs?.Dispose();
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

        // Deleting every sandbox through the provider removes everything it made; anything else, such
        // as a router whose sandbox is gone, would stay on people's engines unnoticed, so it fails the run.
        if (leftBehind.Count > 0)
        {
            throw new InvalidOperationException("The run left containers on the Docker Engine, removed now: " + string.Join(", ", leftBehind));
        }
    }

    // Removes the containers of a scope that outlived its sandboxes; returns their names.
    private static async Task<List<string>> RemoveLeftBehindAsync(string scope)
    {
        using DockerClient docker = new DockerClient(new DockerClientSettings(DockerEndpoint));
        IReadOnlyList<ContainerListItem> left = await docker.ListContainersAsync(["com.bagatka.sandboxing.scope=" + scope], CancellationToken.None);
        List<string> names = [];
        foreach (ContainerListItem container in left)
        {
            ContainerDetails? details = await docker.InspectContainerAsync(container.Id, CancellationToken.None);
            names.Add(details?.Name ?? container.Id);
            await docker.RemoveContainerAsync(container.Id, CancellationToken.None);
        }

        return names;
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
    internal static int FreePort()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    // Whatever tests left behind, including after a failure: sandboxes, and the ready copies' snapshots.
    private static async Task DeleteSandboxesAsync(string scope)
    {
        ServiceCollection services = new ServiceCollection();
        services.AddDockerSandboxProvider(new DockerSandboxSettings(DockerEndpoint, scope));
        await using ServiceProvider provider = services.BuildServiceProvider();
        ISandboxProvider docker = provider.GetRequiredService<ISandboxProvider>();
        await foreach (SandboxObservation sandbox in docker.ListAsync(CancellationToken.None))
        {
            await docker.DeleteAsync(sandbox.Key, CancellationToken.None);
        }

        await foreach (SnapshotObservation snapshot in docker.ListSnapshotsAsync(CancellationToken.None))
        {
            await docker.DeleteSnapshotAsync(snapshot.Key, CancellationToken.None);
        }
    }
}
