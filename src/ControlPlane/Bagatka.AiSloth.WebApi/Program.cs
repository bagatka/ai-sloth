using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Azure.Identity;
using Bagatka.Azure.Sandboxes;
using Bagatka.AiSloth.AgentAccounts;
using Bagatka.AiSloth.Chats;
using Bagatka.AiSloth.Machines;
using Bagatka.AiSloth.Nooks;
using Bagatka.AiSloth.Secrets;
using Bagatka.AiSloth.Sources;
using Bagatka.AiSloth.Users;
using Bagatka.AiSloth.WebApi;
using Bagatka.AiSloth.WebApi.Endpoints;
using Bagatka.AiSloth.Workspaces;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Foundation.Web;
using Bagatka.ObjectStorage;
using Bagatka.ObjectStorage.AzureBlob;
using Bagatka.Sandboxing.Azure;
using Bagatka.Sandboxing.Docker;
using Bagatka.Sdk.GitHub;
using Bagatka.ServiceDefaults;
using Bagatka.AiSloth.Users.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

// Anything that slips past the analyzers formats the same way on every server.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.AddServiceDefaults();

// Every environment checks the container at startup, not only Development: a missing registration
// or a singleton holding a scoped service fails before the first request.
builder.Host.UseDefaultServiceProvider(provider =>
{
    provider.ValidateOnBuild = true;
    provider.ValidateScopes = true;
});

// Npgsql traces its commands and the Azure sandboxes client its calls, so each request's trace shows
// their time.
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource("Npgsql", SandboxesDiagnostics.Name));

// Modules' own measurements, such as how long a message waits for its agent's first action, and Azure
// sandbox calls' durations.
builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddMeter("Bagatka.AiSloth.*", SandboxesDiagnostics.Name));

// The only place that reads configuration (PATTERNS.md, entry 20).
HostSettings host = builder.Configuration.GetRequired<HostSettings>("Host");
SignInProviderSettings? signInProvider = builder.Configuration.GetSection("SignIn:Provider").Exists()
    ? builder.Configuration.GetRequired<SignInProviderSettings>("SignIn:Provider")
    : null;
DockerSandboxSettings? docker = builder.Configuration.GetSection("Sandboxing:Docker").Exists()
    ? builder.Configuration.GetRequired<DockerSandboxSettings>("Sandboxing:Docker")
    : null;
AzureSandboxSettings? azure = builder.Configuration.GetSection("Sandboxing:Azure").Exists()
    ? builder.Configuration.GetRequired<AzureSandboxSettings>("Sandboxing:Azure")
    : null;
DatabaseSettings database = builder.Configuration.GetRequired<DatabaseSettings>("Database");
EncryptionSettings encryption = builder.Configuration.GetRequired<EncryptionSettings>("Encryption");
NooksSettings nooks = builder.Configuration.GetRequired<NooksSettings>("Modules:Nooks");
AgentAccountsSettings agentAccounts = builder.Configuration.GetRequired<AgentAccountsSettings>("Modules:AgentAccounts");
SourcesSettings sources = builder.Configuration.GetSection("Modules:Sources").Exists() ? builder.Configuration.GetRequired<SourcesSettings>("Modules:Sources") : new SourcesSettings();
GitHubSettings gitHub = builder.Configuration.GetSection("GitHub").Exists() ? builder.Configuration.GetRequired<GitHubSettings>("GitHub") : GitHubSettings.Public;
ChatsSettings chats = builder.Configuration.GetRequired<ChatsSettings>("Modules:Chats");
FileSystemObjectStorageSettings? folderStorage = builder.Configuration.GetSection("ObjectStorage:FileSystem").Exists()
    ? builder.Configuration.GetRequired<FileSystemObjectStorageSettings>("ObjectStorage:FileSystem")
    : null;
AzureBlobObjectStorageSettings? blobStorage = builder.Configuration.GetSection("ObjectStorage:AzureBlob").Exists()
    ? builder.Configuration.GetRequired<AzureBlobObjectStorageSettings>("ObjectStorage:AzureBlob")
    : null;
if ((folderStorage is null) == (blobStorage is null))
{
    throw new InvalidOperationException("Configure exactly one of the sections 'ObjectStorage:FileSystem' and 'ObjectStorage:AzureBlob'.");
}

// One Azure sign-in for whatever the host uses in Azure: its managed identity when hosted, or the
// developer's Azure CLI.
DefaultAzureCredential azureCredential = new DefaultAzureCredential();
ModelGatewaySettings modelGateway = builder.Configuration.GetSection("ModelGateway").Exists() ? builder.Configuration.GetRequired<ModelGatewaySettings>("ModelGateway") : new ModelGatewaySettings();

builder.Services.AddSingleton(TimeProvider.System);

// One instance at a time does background work (ActiveInstance). One told to stop hands it over at
// once, then lets requests in flight finish, agents' model calls among them, for up to this long;
// the deployment waits a little longer before it kills the instance (the AppHost's grace period).
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(570));
builder.Services.AddModuleDatabase(database);
builder.Services.AddActiveInstance();
builder.Services.ConfigureHttpJsonOptions(json => FoundationJson.Configure(json.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddGrpc();
builder.Services.AddSingleton(modelGateway);

// Model calls stream for as long as the agent's turn needs; the agent cancels, never a timeout here.
// Endpoints are people's choice, so connections reach only the public internet unless the deployment
// allows private networks, and redirects go back to the agent instead of being followed.
builder.Services.AddHttpClient(ModelGatewayEndpoints.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        ConnectCallback = modelGateway.AllowPrivateNetworks ? null : PublicNetworks.ConnectAsync,
    });

// Each call carries its device's session; Users decides whose it is. People sign in with codes, or
// through the host's identity provider when it has one (Endpoints/SignInEndpoints.cs), whose browser
// round trip Data Protection keeps safe instead of state here.
builder.Services.AddSingleton(host);
builder.Services.AddDataProtection();
builder.Services.AddAuthentication(SessionAuthentication.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, SessionAuthentication>(SessionAuthentication.SchemeName, configureOptions: null);
if (signInProvider is not null)
{
    builder.Services.AddSingleton(signInProvider);
    builder.Services.AddSingleton<SignInProvider>();
    builder.Services.AddHttpClient(SignInProvider.HttpClientName);
}

// Every endpoint requires a signed-in user unless it says AllowAnonymous().
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
builder.Services.AddRateLimits(host);

// Sources copies repositories in and pushes them out with people's GitHub connections, through the
// host's GitHub App, running git on this computer.
builder.Services.AddGitHubClient(gitHub);

// Checkpoints are kept in a folder of this computer, or in Azure Blob Storage when hosted.
if (folderStorage is not null)
{
    builder.Services.AddFileSystemObjectStorage(folderStorage);
}
else
{
    builder.Services.AddAzureBlobObjectStorage(blobStorage!, azureCredential);
}

// Azure Container Apps Sandboxes when configured, signed in as the host's managed identity or the
// developer's Azure CLI.
if (azure is not null)
{
    builder.Services.AddAzureSandboxProvider(azure, azureCredential);
}

// Nooks run on a Docker Engine too when the host has one, such as in development or on one server.
// They reach the control plane through the Docker host when its addresses for them name it, and
// nothing else there.
if (docker is not null)
{
    Uri[] nookFacing = [nooks.DaemonUrl, chats.ModelGatewayUrl];
    builder.Services.AddDockerSandboxProvider(docker with
    {
        HostPorts = [.. nookFacing.Where(url => string.Equals(url.Host, "host.docker.internal", StringComparison.Ordinal)).Select(url => url.Port)],
    });
}

builder.Services
    .AddUsersModule()
    .AddWorkspacesModule()
    .AddMachinesModule()
    .AddSecretsModule(encryption)
    .AddSourcesModule(sources, encryption)
    .AddNooksModule(nooks)
    .AddAgentAccountsModule(agentAccounts, encryption)
    .AddChatsModule(chats);

await using WebApplication app = builder.Build();

// `migrate` applies every module's migrations and exits: the deployment step that runs before a new
// version starts (PATTERNS.md, entry 14). The AppHost runs it before the WebApi.
if (args is ["migrate"])
{
    await ModuleDatabases.MigrateAsync(app.Services, CancellationToken.None);
    return;
}

// Behind a proxy, the caller's address is the one it adds last; one a caller wrote is never trusted.
if (host.BehindProxy)
{
    ForwardedHeadersOptions forwarded = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor, ForwardLimit = 1 };
    forwarded.KnownIPNetworks.Clear();
    forwarded.KnownProxies.Clear();
    app.UseForwardedHeaders(forwarded);
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// While nobody has signed up, the host's first person signs in with a setup code. It goes to
// standard output only, never through logging, so no telemetry carries it.
await using (AsyncServiceScope setup = app.Services.CreateAsyncScope())
{
    string? setupCode = await setup.ServiceProvider.GetRequiredService<IUsersApi>().OpenSetupAsync(Actor.ForSystem("webapi.setup"), CancellationToken.None);
    if (setupCode is not null)
    {
        await Console.Out.WriteLineAsync("First sign-in: sloth host add " + host.PublicUrl.AbsoluteUri.TrimEnd('/') + " --code " + setupCode + "   (valid for a day, once)");
    }
}

app.MapDefaultEndpoints();
app.MapSignInEndpoints();
app.MapOpenApi().AllowAnonymous();
app.MapUsersEndpoints();
app.MapWorkspacesEndpoints();
app.MapAccessEndpoints();
app.MapMachinesEndpoints();
app.MapNooksEndpoints();
app.MapAgentAccountsEndpoints();
app.MapSecretsEndpoints();
app.MapSourcesEndpoints();
app.MapChatsEndpoints();

// Kestrel's endpoints are the AppHost's choice. Agents in nooks reach the model gateway: run locally
// on the "Models" endpoint, and deployed on the one public endpoint. A call carries its chat's token
// instead of a user's.
app.MapModelGateway();

// Daemons and machines dial an HTTP/2 endpoint: run locally the "Daemon" endpoint, and deployed the
// one public endpoint. They prove themselves with their own token instead of a user's.
app.MapGrpcService<DaemonEndpoint>().AllowAnonymous();
app.MapGrpcService<MachineEndpoint>().AllowAnonymous();

await app.RunAsync();
