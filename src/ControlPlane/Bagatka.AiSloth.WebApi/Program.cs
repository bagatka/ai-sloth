using System;
using System.Globalization;
using System.Threading;
using Bagatka.AiSloth.Chats;
using Bagatka.AiSloth.Machines;
using Bagatka.AiSloth.Nooks;
using Bagatka.AiSloth.Users;
using Bagatka.AiSloth.WebApi;
using Bagatka.AiSloth.WebApi.Endpoints;
using Bagatka.AiSloth.Workspaces;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sandboxing.Docker;
using Bagatka.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
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
AuthenticationSettings authentication = builder.Configuration.GetRequired<AuthenticationSettings>("Authentication");
DockerSandboxSettings docker = builder.Configuration.GetRequired<DockerSandboxSettings>("Sandboxing:Docker");
UsersSettings users = builder.Configuration.GetRequired<UsersSettings>("Modules:Users");
WorkspacesSettings workspaces = builder.Configuration.GetRequired<WorkspacesSettings>("Modules:Workspaces");
MachinesSettings machines = builder.Configuration.GetRequired<MachinesSettings>("Modules:Machines");
NooksSettings nooks = builder.Configuration.GetRequired<NooksSettings>("Modules:Nooks");
ChatsSettings chats = builder.Configuration.GetRequired<ChatsSettings>("Modules:Chats");
ModelGatewaySettings modelGateway = builder.Configuration.GetRequired<ModelGatewaySettings>("ModelGateway");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(json => FoundationJson.Configure(json.SerializerOptions));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddGrpc();
builder.Services.AddSingleton(modelGateway);

// Model calls stream for as long as the agent's turn needs; the agent cancels, never a timeout here.
builder.Services.AddHttpClient(ModelGatewayEndpoints.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);

// Tokens from the configured OpenID Connect provider; who the token's subject is, Users decides.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(jwt =>
    {
        jwt.Authority = authentication.Issuer.AbsoluteUri;
        jwt.Audience = authentication.Audience;
        jwt.RequireHttpsMetadata = string.Equals(authentication.Issuer.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);

        // Claims keep the token's names, such as "sub".
        jwt.MapInboundClaims = false;
        jwt.Events = new JwtBearerEvents { OnTokenValidated = TokenSignIn.RecordUserAsync };
    });

// Every endpoint requires a signed-in user unless it says AllowAnonymous().
builder.Services.AddAuthorizationBuilder()
    .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services
    .AddDockerSandboxProvider(docker)
    .AddUsersModule(users)
    .AddWorkspacesModule(workspaces)
    .AddMachinesModule(machines)
    .AddNooksModule(nooks)
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

app.MapDefaultEndpoints();
app.MapOpenApi().AllowAnonymous();
app.MapUsersEndpoints();
app.MapWorkspacesEndpoints();
app.MapMachinesEndpoints();
app.MapNooksEndpoints();
app.MapChatsEndpoints();

// Agents in nooks reach the model gateway on Kestrel's "Models" endpoint (appsettings.json); a call
// carries its chat's token instead of a user's.
app.MapModelGateway();

// Daemons and machines dial Kestrel's HTTP/2-only "Daemon" endpoint (appsettings.json) and prove
// themselves with their own token instead of a user's.
app.MapGrpcService<DaemonEndpoint>().AllowAnonymous();
app.MapGrpcService<MachineEndpoint>().AllowAnonymous();

await app.RunAsync();
