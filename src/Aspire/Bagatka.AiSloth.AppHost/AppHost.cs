using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);
string repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".."));
const string NookImage = "aisloth-nook:dev";
// The harnesses a nook can carry: the profiles in src/Harnesses, one image each.
string[] harnesses = ["claude-code", "codex", "pi", "copilot"];

// Sign-in: with codes always (the WebApi prints the first person's setup code to its console), and
// with an OpenID Connect provider when one is configured, such as a WorkOS staging environment: set
// sign-in-provider-issuer, -client-id, -client-secret, and -name as user secrets of this project.
// Tests configure a fake one, and let invites sign people up beside it.
string? providerIssuer = builder.Configuration["Parameters:sign-in-provider-issuer"];
string? inviteSignUp = builder.Configuration["Parameters:invite-sign-up"];

// Agents call their models through the WebApi's model gateway, which forwards each call to its chat's
// agent account's endpoint. Endpoints must be public https ones unless private networks are allowed,
// for a model running on this machine, or tests.
IResourceBuilder<ParameterResource> modelPrivateNetworks = builder.AddParameter(
    "model-private-networks", builder.Configuration["Parameters:model-private-networks"] ?? "false");

// Running AiSloth for yourself is the self-hosted use OpenAI allows ChatGPT plans for. Tests point
// Sign in with ChatGPT and the plans' API at fakes.
IResourceBuilder<ParameterResource> allowChatGptPlans = builder.AddParameter(
    "allow-chatgpt-plans", builder.Configuration["Parameters:allow-chatgpt-plans"] ?? "true");
string? chatGptAuthority = builder.Configuration["Parameters:chatgpt-authority"];
string? chatGptApi = builder.Configuration["Parameters:chatgpt-api"];

// Encrypts agent accounts' secrets at rest; generated once and kept in this project's user secrets.
IResourceBuilder<ParameterResource> agentAccountsKey = builder.AddParameter(
    "agent-accounts-key", new GenerateParameterDefault { MinLength = 48, Special = false }, secret: true, persist: true);

// People connect GitHub through the host's GitHub App, which `sloth github create-app` makes: give
// github-app-client-id, -client-secret, and -slug as user secrets of this project. Tests point
// github-api and github-web at a fake GitHub.
string? gitHubAppClientId = builder.Configuration["Parameters:github-app-client-id"];
string? gitHubApi = builder.Configuration["Parameters:github-api"];

// Encrypts people's GitHub tokens at rest; generated once and kept in this project's user secrets.
IResourceBuilder<ParameterResource> sourcesKey = builder.AddParameter(
    "sources-key", new GenerateParameterDefault { MinLength = 48, Special = false }, secret: true, persist: true);

// Encrypts secrets' values at rest; generated once and kept in this project's user secrets.
IResourceBuilder<ParameterResource> secretsKey = builder.AddParameter(
    "secrets-key", new GenerateParameterDefault { MinLength = 48, Special = false }, secret: true, persist: true);

// Where checkpoints are kept: a folder of this computer, unless tests give their own.
IResourceBuilder<ParameterResource> objectStorage = builder.AddParameter(
    "object-storage",
    builder.Configuration["Parameters:object-storage"]
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "aisloth", "objects"));

// How full a nook's disk is when a message for its agent needs confirming. Docker nooks share this
// computer's disk, so tests ask only for a full one.
IResourceBuilder<ParameterResource> nearlyFullDisk = builder.AddParameter(
    "nearly-full-disk", builder.Configuration["Parameters:nearly-full-disk"] ?? "0.9");

// The Docker scope nooks run in, so test runs never touch a developer's nooks. A parameter given a
// value can't be overridden, so the default is applied here.
IResourceBuilder<ParameterResource> sandboxScope = builder.AddParameter("sandbox-scope", builder.Configuration["Parameters:sandbox-scope"] ?? "dev");

// The Docker Engine nooks run in and their images are built in: DOCKER_HOST's, as for the docker
// command, or the default one. It needs Sysbox (src/Sandboxing/README.md).
string dockerHost = builder.Configuration["DOCKER_HOST"] is { Length: > 0 } host ? host : "unix:///var/run/docker.sock";

IResourceBuilder<PostgresDatabaseResource> database = builder.AddPostgres("postgres")
    .WithImageTag("18")
    .AddDatabase("aisloth");

// The images nooks start from: the base, and one per harness on top of it. Docker's cache makes a
// rebuild without changes take seconds.
IResourceBuilder<ExecutableResource> nookImage = builder.AddExecutable(
    "nook-image", "docker", repositoryRoot, "build", "--file", "src/Daemon/Dockerfile", "--target", "nook", "--tag", NookImage, ".")
    .WithEnvironment("DOCKER_HOST", dockerHost);
IResourceBuilder<ExecutableResource>[] harnessImages = [.. harnesses.Select(harness => builder.AddExecutable(
        "nook-image-" + harness, "docker", repositoryRoot, "build", "--file", "src/Daemon/Dockerfile", "--target", harness, "--tag", HarnessImage(harness), ".")
    .WithEnvironment("DOCKER_HOST", dockerHost)
    .WaitForCompletion(nookImage))];

IResourceBuilder<ProjectResource> webApi = builder.AddProject<Projects.Bagatka_AiSloth_WebApi>("webapi")
    .WithHttpHealthCheck("/health", endpointName: "Http");
EndpointReference daemonEndpoint = NookFacing("Daemon", builder.Configuration["DaemonPort"]);
EndpointReference modelsEndpoint = NookFacing("Models", builder.Configuration["ModelsPort"]);

// The WebApi in migration mode: applies every module's migrations, then exits.
IResourceBuilder<ProjectResource> migrations = builder.AddProject<Projects.Bagatka_AiSloth_WebApi>("migrations", options => options.ExcludeKestrelEndpoints = true)
    .WithArgs("migrate")
    .WaitFor(database);

// Both modes read the same settings (PATTERNS.md, entry 20). Nooks reach the daemon endpoint
// through the Docker host.
foreach (IResourceBuilder<ProjectResource> mode in new[] { webApi, migrations })
{
    mode.WithEnvironment("Host__PublicUrl", webApi.GetEndpoint("Http"))
        .WithEnvironment("Modules__Users__ConnectionString", database.Resource.ConnectionStringExpression)
        .WithEnvironment("Modules__Workspaces__ConnectionString", database.Resource.ConnectionStringExpression)
        .WithEnvironment("Modules__Machines__ConnectionString", database.Resource.ConnectionStringExpression)
        .WithEnvironment("Modules__Nooks__ConnectionString", database.Resource.ConnectionStringExpression)
        .WithEnvironment("Modules__Chats__ConnectionString", database.Resource.ConnectionStringExpression)
        .WithEnvironment("Modules__Chats__ModelGatewayUrl", ReferenceExpression.Create($"http://host.docker.internal:{modelsEndpoint.Property(EndpointProperty.Port)}/models"))
        .WithEnvironment("Modules__Chats__NearlyFullDisk", nearlyFullDisk)
        .WithEnvironment("Modules__AgentAccounts__ConnectionString", database.Resource.ConnectionStringExpression)
        .WithEnvironment("Modules__AgentAccounts__EncryptionKey", agentAccountsKey)
        .WithEnvironment("Modules__AgentAccounts__AllowChatGptPlans", allowChatGptPlans)
        .WithEnvironment("Modules__Secrets__ConnectionString", database.Resource.ConnectionStringExpression)
        .WithEnvironment("Modules__Secrets__EncryptionKey", secretsKey)
        .WithEnvironment("Modules__Sources__ConnectionString", database.Resource.ConnectionStringExpression)
        .WithEnvironment("Modules__Sources__EncryptionKey", sourcesKey)
        .WithEnvironment("ModelGateway__AllowPrivateNetworks", modelPrivateNetworks)
        .WithEnvironment("Modules__Nooks__DaemonUrl", ReferenceExpression.Create($"http://host.docker.internal:{daemonEndpoint.Property(EndpointProperty.Port)}"))
        .WithEnvironment("Modules__Nooks__Image", NookImage)
        .WithEnvironment("ObjectStorage__FileSystem__Root", objectStorage)
        .WithEnvironment(environment =>
        {
            foreach (string harness in harnesses)
            {
                environment.EnvironmentVariables["Modules__Nooks__HarnessImages__" + harness] = HarnessImage(harness);
            }

            if (providerIssuer is not null)
            {
                environment.EnvironmentVariables["SignIn__Provider__Issuer"] = providerIssuer;
                environment.EnvironmentVariables["SignIn__Provider__ClientId"] = builder.Configuration["Parameters:sign-in-provider-client-id"] ?? string.Empty;
                environment.EnvironmentVariables["SignIn__Provider__ClientSecret"] = builder.Configuration["Parameters:sign-in-provider-client-secret"] ?? string.Empty;
                environment.EnvironmentVariables["SignIn__Provider__Name"] = builder.Configuration["Parameters:sign-in-provider-name"] ?? string.Empty;
            }

            if (inviteSignUp is not null)
            {
                environment.EnvironmentVariables["Host__InviteSignUp"] = inviteSignUp;
            }

            if (gitHubAppClientId is not null)
            {
                environment.EnvironmentVariables["Modules__Sources__GitHubApp__ClientId"] = gitHubAppClientId;
                environment.EnvironmentVariables["Modules__Sources__GitHubApp__ClientSecret"] = builder.Configuration["Parameters:github-app-client-secret"] ?? string.Empty;
                environment.EnvironmentVariables["Modules__Sources__GitHubApp__Slug"] = builder.Configuration["Parameters:github-app-slug"] ?? string.Empty;
            }

            if (gitHubApi is not null)
            {
                environment.EnvironmentVariables["GitHub__ApiUrl"] = gitHubApi;
                environment.EnvironmentVariables["GitHub__WebUrl"] = builder.Configuration["Parameters:github-web"] ?? string.Empty;
            }

            if (chatGptAuthority is not null)
            {
                environment.EnvironmentVariables["Modules__AgentAccounts__ChatGptAuthority"] = chatGptAuthority;
            }

            if (chatGptApi is not null)
            {
                environment.EnvironmentVariables["Modules__AgentAccounts__ChatGptApi"] = chatGptApi;
            }
        })
        .WithEnvironment("Modules__Nooks__CpuMillicores", "2000")
        .WithEnvironment("Modules__Nooks__MemoryMebibytes", "4096")
        .WithEnvironment("Sandboxing__Docker__Endpoint", dockerHost)
        .WithEnvironment("Sandboxing__Docker__Scope", sandboxScope);
}

webApi.WaitForCompletion(migrations).WaitForCompletion(nookImage);
foreach (IResourceBuilder<ExecutableResource> harnessImage in harnessImages)
{
    webApi.WaitForCompletion(harnessImage);
}

using DistributedApplication app = builder.Build();
app.Run();

static string HarnessImage(string harness)
{
    return "aisloth-nook-" + harness + ":dev";
}

// Nooks are containers that reach these endpoints through the Docker host's gateway. On Linux the
// gateway isn't localhost, where Aspire's proxy and Kestrel would listen, so the WebApi listens on
// every IPv4 interface itself; Docker Desktop on WSL doesn't forward to dual-stack (`*`) listeners.
// Without the proxy a port is fixed (appsettings.json), so tests pass free ones.
EndpointReference NookFacing(string name, string? port)
{
    webApi.WithEndpoint(name, endpoint =>
    {
        endpoint.IsProxied = false;
        if (port is not null)
        {
            endpoint.Port = int.Parse(port, CultureInfo.InvariantCulture);
            endpoint.TargetPort = endpoint.Port;
        }
    });
    EndpointReference reference = webApi.GetEndpoint(name);
    webApi.WithEnvironment("Kestrel__Endpoints__" + name + "__Url", ReferenceExpression.Create($"http://0.0.0.0:{reference.Property(EndpointProperty.TargetPort)}"));
    return reference;
}
