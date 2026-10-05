using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
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
using Bagatka.Sandboxing.Docker;
using Bagatka.Sdk.GitHub;
using Bagatka.ServiceDefaults;
using Bagatka.AiSloth.Users.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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

// Npgsql traces its commands; collecting them puts database time into each request's trace.
builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddSource("Npgsql"));

// The only place that reads configuration (PATTERNS.md, entry 20).
HostSettings host = builder.Configuration.GetRequired<HostSettings>("Host");
SignInProviderSettings? signInProvider = builder.Configuration.GetSection("SignIn:Provider").Exists()
    ? builder.Configuration.GetRequired<SignInProviderSettings>("SignIn:Provider")
    : null;
DockerSandboxSettings docker = builder.Configuration.GetRequired<DockerSandboxSettings>("Sandboxing:Docker");
UsersSettings users = builder.Configuration.GetRequired<UsersSettings>("Modules:Users");
WorkspacesSettings workspaces = builder.Configuration.GetRequired<WorkspacesSettings>("Modules:Workspaces");
MachinesSettings machines = builder.Configuration.GetRequired<MachinesSettings>("Modules:Machines");
NooksSettings nooks = builder.Configuration.GetRequired<NooksSettings>("Modules:Nooks");
AgentAccountsSettings agentAccounts = builder.Configuration.GetRequired<AgentAccountsSettings>("Modules:AgentAccounts");
SecretsSettings secrets = builder.Configuration.GetRequired<SecretsSettings>("Modules:Secrets");
SourcesSettings sources = builder.Configuration.GetRequired<SourcesSettings>("Modules:Sources");
GitHubSettings gitHub = builder.Configuration.GetSection("GitHub").Exists() ? builder.Configuration.GetRequired<GitHubSettings>("GitHub") : GitHubSettings.Public;
ChatsSettings chats = builder.Configuration.GetRequired<ChatsSettings>("Modules:Chats");
ModelGatewaySettings modelGateway = builder.Configuration.GetRequired<ModelGatewaySettings>("ModelGateway");

builder.Services.AddSingleton(TimeProvider.System);
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

// Sources copies repositories in and pushes them out with people's GitHub connections, through the
// host's GitHub App, running git on this computer.
builder.Services.AddGitHubClient(gitHub);

builder.Services
    .AddDockerSandboxProvider(docker)
    .AddUsersModule(users)
    .AddWorkspacesModule(workspaces)
    .AddMachinesModule(machines)
    .AddSecretsModule(secrets)
    .AddSourcesModule(sources)
    .AddNooksModule(nooks)
    .AddAgentAccountsModule(agentAccounts)
    .AddChatsModule(chats);

await using WebApplication app = builder.Build();

// `migrate` applies every module's migrations and exits: the deployment step that runs before a new
// version starts (PATTERNS.md, entry 14). The AppHost runs it before the WebApi.
if (args is ["migrate"])
{
    await ModuleDatabases.MigrateAsync(app.Services, CancellationToken.None);
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

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

// Agents in nooks reach the model gateway on Kestrel's "Models" endpoint (appsettings.json); a call
// carries its chat's token instead of a user's.
app.MapModelGateway();

// Daemons and machines dial Kestrel's HTTP/2-only "Daemon" endpoint (appsettings.json) and prove
// themselves with their own token instead of a user's.
app.MapGrpcService<DaemonEndpoint>().AllowAnonymous();
app.MapGrpcService<MachineEndpoint>().AllowAnonymous();

await app.RunAsync();
