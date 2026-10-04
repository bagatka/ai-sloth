using System;
using System.Globalization;
using System.Threading;
using Bagatka.AiSloth.Nooks;
using Bagatka.AiSloth.WebApi.Endpoints;
using Bagatka.AiSloth.Workspaces;
using Bagatka.Foundation.Modules;
using Bagatka.Sandboxing.Docker;
using Bagatka.ServiceDefaults;
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
DockerSandboxSettings docker = builder.Configuration.GetRequired<DockerSandboxSettings>("Sandboxing:Docker");
WorkspacesSettings workspaces = builder.Configuration.GetRequired<WorkspacesSettings>("Modules:Workspaces");
NooksSettings nooks = builder.Configuration.GetRequired<NooksSettings>("Modules:Nooks");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddGrpc();
builder.Services
    .AddDockerSandboxProvider(docker)
    .AddWorkspacesModule(workspaces)
    .AddNooksModule(nooks);

await using WebApplication app = builder.Build();

// `migrate` applies every module's migrations and exits: the deployment step that runs before a new
// version starts (PATTERNS.md, entry 14). The AppHost runs it before the WebApi.
if (args is ["migrate"])
{
    await ModuleDatabases.MigrateAsync(app.Services, CancellationToken.None);
    return;
}

app.MapDefaultEndpoints();

// Daemons dial Kestrel's HTTP/2-only "Daemon" endpoint (appsettings.json).
app.MapGrpcService<DaemonEndpoint>();

await app.RunAsync();
