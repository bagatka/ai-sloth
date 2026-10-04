using System.IO;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);
string repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".."));
const string NookImage = "aisloth-nook:dev";

IResourceBuilder<PostgresDatabaseResource> database = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .AddDatabase("aisloth");

// The image every nook starts from. Docker's cache makes a rebuild without changes take seconds.
IResourceBuilder<ExecutableResource> nookImage = builder.AddExecutable(
    "nook-image", "docker", repositoryRoot, "build", "--file", "src/Daemon/Dockerfile", "--tag", NookImage, ".");

IResourceBuilder<ProjectResource> webApi = builder.AddProject<Projects.Bagatka_AiSloth_WebApi>("webapi")
    .WithHttpHealthCheck("/health", endpointName: "Http");

// The WebApi in migration mode: applies every module's migrations, then exits.
IResourceBuilder<ProjectResource> migrations = builder.AddProject<Projects.Bagatka_AiSloth_WebApi>("migrations", options => options.ExcludeKestrelEndpoints = true)
    .WithArgs("migrate")
    .WaitFor(database);

// Both modes read the same settings (PATTERNS.md, entry 20). Nooks reach the daemon endpoint
// through the Docker host.
EndpointReference daemonEndpoint = webApi.GetEndpoint("Daemon");
foreach (IResourceBuilder<ProjectResource> mode in new[] { webApi, migrations })
{
    mode.WithEnvironment("Modules__Workspaces__ConnectionString", database.Resource.ConnectionStringExpression)
        .WithEnvironment("Modules__Nooks__ConnectionString", database.Resource.ConnectionStringExpression)
        .WithEnvironment("Modules__Nooks__DaemonUrl", ReferenceExpression.Create($"http://host.docker.internal:{daemonEndpoint.Property(EndpointProperty.Port)}"))
        .WithEnvironment("Modules__Nooks__Image", NookImage)
        .WithEnvironment("Modules__Nooks__CpuMillicores", "2000")
        .WithEnvironment("Modules__Nooks__MemoryMebibytes", "4096")
        .WithEnvironment("Sandboxing__Docker__Endpoint", "unix:///var/run/docker.sock")
        .WithEnvironment("Sandboxing__Docker__Scope", "dev");
}

webApi.WaitForCompletion(migrations).WaitForCompletion(nookImage);

using DistributedApplication app = builder.Build();
app.Run();
