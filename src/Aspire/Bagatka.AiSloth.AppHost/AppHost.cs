using System;
using System.Globalization;
using System.IO;
using System.Linq;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Storage;
using Bagatka.AiSloth.AppHost;

IDistributedApplicationBuilder builder = DistributedApplication.CreateBuilder(args);
string repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".."));
// The harnesses a nook can carry: the profiles in src/Harnesses, one image each; nook-harnesses names
// fewer, comma-separated, such as for tests that push their images somewhere.
string[] harnesses = builder.Configuration["Parameters:nook-harnesses"] is { Length: > 0 } chosen
    ? chosen.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
    : ["claude-code", "codex", "pi", "copilot"];

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

// Running AiSloth for yourself is the self-hosted use OpenAI allows ChatGPT plans for.
IResourceBuilder<ParameterResource> allowChatGptPlans = builder.AddParameter(
    "allow-chatgpt-plans", builder.Configuration["Parameters:allow-chatgpt-plans"] is { Length: > 0 } allowed ? allowed : "true");

// Encrypts agent accounts' secrets, people's GitHub tokens, and secrets' values at rest; losing it
// makes them unreadable. Run here, it is generated once and kept in this project's user secrets.
// Deploying never generates it: a deployment without its key stops instead of encrypting with a new
// one (docs/self-hosting.md).
IResourceBuilder<ParameterResource> encryptionKey = builder.ExecutionContext.IsPublishMode
    ? builder.AddParameter("encryption-key", secret: true)
    : builder.AddParameter("encryption-key", new GenerateParameterDefault { MinLength = 48, Special = false }, secret: true, persist: true);

// People connect GitHub through the host's GitHub App, which `sloth github create-app` makes: give
// github-app-client-id, -client-secret, and -slug as user secrets of this project.
string? gitHubAppClientId = builder.Configuration["Parameters:github-app-client-id"];

// The scope nooks run in, which the control plane owns: it deletes sandboxes there that no nook
// records. Run here, each clone gets its own, generated once and kept in its user secrets, so
// developers sharing a sandbox group and test runs never touch each other's nooks. A parameter given
// a value can't be overridden, so the configured one is applied here.
string? configuredScope = builder.Configuration["Parameters:sandbox-scope"];
IResourceBuilder<ParameterResource> sandboxScope = configuredScope is not null || builder.ExecutionContext.IsPublishMode
    ? builder.AddParameter("sandbox-scope", configuredScope ?? "dev")
    : builder.AddParameter("sandbox-scope", new GenerateParameterDefault { MinLength = 12, Upper = false, Special = false }, persist: true);

// The repository nook images are pushed to, which nooks outside this computer pull them from.
string? imageRepository = builder.Configuration["Parameters:nook-image-repository"];

// The images' tag: dev for images built here, or the commit whose images main published
// (docs/self-hosting.md).
string imageTag = builder.Configuration["Parameters:nook-image-tag"] ?? "dev";
string nookImage = "aisloth-nook:" + imageTag;

// The Docker Engine nooks run in and their images are built in when run here: DOCKER_HOST's, as for
// the docker command, or the default one. It needs Sysbox (src/Sandboxing/README.md).
string dockerHost = builder.Configuration["DOCKER_HOST"] is { Length: > 0 } host ? host : "unix:///var/run/docker.sock";

// What differs between running here and deployed: what runs the WebApi and where it listens, the
// database, where checkpoints are kept, where nooks run, and the addresses they reach the WebApi at.
IResourceBuilder<IResourceWithEnvironment>[] modes;
ReferenceExpression database;
ReferenceExpression publicUrl;
ReferenceExpression daemonUrl;
ReferenceExpression modelsUrl;
if (builder.ExecutionContext.IsPublishMode)
{
    // Deployed with `dotnet aspire deploy` (docs/self-hosting.md): the WebApi runs in Azure
    // Container Apps, nooks in a sandbox group beside it, and checkpoints are kept in Blob Storage, all
    // in one resource group. Nooks start from images in a public repository.
    if (imageRepository is null)
    {
        throw new InvalidOperationException("Deploying needs nook-image-repository: a public repository with the nook images.");
    }

    builder.AddAzureContainerAppEnvironment("apps");
    IResourceBuilder<AzureUserAssignedIdentityResource> identity = builder.AddAzureUserAssignedIdentity("webapi-identity");
    IResourceBuilder<AzureStorageResource> storage = builder.AddAzureStorage("storage");
    NookSandboxGroup.MakeWhenDeploying(builder, identity.Resource);

    // Any Postgres by its connection string, or else a Flexible Server in the resource group. Settings
    // left empty, as a deploy workflow passes ones a fork doesn't set, count as not given.
    database = builder.Configuration["Parameters:postgres-connection-string"] is { Length: > 0 }
        ? ReferenceExpression.Create($"{builder.AddParameter("postgres-connection-string", secret: true)}")
        : builder.AddAzurePostgresFlexibleServer("postgres").WithPasswordAuthentication().AddDatabase("aisloth").Resource.ConnectionStringExpression;

    // A custom domain, such as app.example.com, comes with the name of its managed certificate.
    IResourceBuilder<ParameterResource>? customDomain = builder.Configuration["Parameters:custom-domain"] is { Length: > 0 }
        ? builder.AddParameter("custom-domain")
        : null;
    IResourceBuilder<ParameterResource>? customDomainCertificate = customDomain is not null
        ? builder.AddParameter("custom-domain-certificate")
        : null;

    // One public HTTPS address for the API, daemons, machines, and the model gateway: Container Apps'
    // ingress ends TLS and speaks HTTP/2 to Kestrel's "Public" endpoint, as gRPC needs. Health probes
    // speak HTTP/1.1, so they get an endpoint of their own, which ingress doesn't expose. Git is in the
    // image for Sources, so it's built from a Dockerfile.
    IResourceBuilder<ContainerResource> webApi = builder.AddDockerfile("webapi", repositoryRoot, "src/ControlPlane/Bagatka.AiSloth.WebApi/Dockerfile")
        .WithHttpEndpoint(targetPort: 8080, name: "Public")
        .WithEndpoint("Public", endpoint =>
        {
            endpoint.Transport = "http2";
            endpoint.IsExternal = true;
        })
        .WithEnvironment("Kestrel__Endpoints__Public__Url", "http://+:8080")
        .WithEnvironment("Kestrel__Endpoints__Public__Protocols", "Http2")
        .WithEnvironment("Kestrel__Endpoints__Health__Url", "http://+:8081")
        .WithEnvironment("Kestrel__Endpoints__Health__Protocols", "Http1")
        .WithAzureUserAssignedIdentity(identity)
        .WithRoleAssignments(storage, StorageBuiltInRole.StorageBlobDataContributor)
        .WithEnvironment("ObjectStorage__AzureBlob__ContainerUrl", ReferenceExpression.Create($"{storage.Resource.BlobEndpoint}checkpoints"))
        .WithEnvironment("Host__BehindProxy", "true")
        .WithEnvironment("Sandboxing__Azure__SandboxGroup", NookSandboxGroup.Name)
        .WithEnvironment("Sandboxing__Azure__Scope", sandboxScope)
        .PublishAsAzureContainerApp((infrastructure, app) =>
        {
            // At most one replica: background work and daemons' connections aren't shared between
            // replicas yet. None while nobody uses the host: the first request starts it, waiting some
            // seconds longer, and requests in flight keep it, such as awake nooks' daemons' and
            // machines' connections. Background work for what sleeps, such as evicting nooks and
            // deleting expired drafts, waits until it starts again. A deploy runs the new one beside the
            // old one, which hands its work over and then gets the longest grace Container Apps allows
            // to finish requests in flight, such as agents' model calls (the WebApi's shutdown timeout
            // is a little shorter).
            app.Template.Scale.MinReplicas = 0;
            app.Template.Scale.MaxReplicas = 1;
            app.Template.TerminationGracePeriodSeconds = 600;

            // The sandbox group is in the deployment's own subscription, resource group, and location.
            ContainerAppContainer container = app.Template.Containers.Single().Value!;

            // Checkpoints, downloads, and copies of repositories pass through this replica's disk, which
            // Container Apps sizes with its CPU: more than 1 vCPU comes with 8 GiB, room for the two
            // large outputs and two copies of repositories it holds at once. Measured, the host uses a
            // seventh of this memory and under half this CPU at its busiest.
            container.Resources = new AppContainerResources { Cpu = 1.25, Memory = "2.5Gi" };
            container.Env.Add(new ContainerAppEnvironmentVariable { Name = "Sandboxing__Azure__SubscriptionId", Value = BicepFunction.GetSubscription().SubscriptionId });
            container.Env.Add(new ContainerAppEnvironmentVariable { Name = "Sandboxing__Azure__ResourceGroup", Value = BicepFunction.GetResourceGroup().Name });
            container.Env.Add(new ContainerAppEnvironmentVariable { Name = "Sandboxing__Azure__Region", Value = BicepFunction.GetResourceGroup().Location });
            container.Probes.Add(new ContainerAppProbe
            {
                ProbeType = ContainerAppProbeType.Liveness,
                HttpGet = new ContainerAppHttpRequestInfo { Path = "/alive", Port = 8081 },
            });
            container.Probes.Add(new ContainerAppProbe
            {
                ProbeType = ContainerAppProbeType.Readiness,
                HttpGet = new ContainerAppHttpRequestInfo { Path = "/health", Port = 8081 },
            });

            // Migrations are applied before a replica starts (PATTERNS.md, entry 14).
            app.Template.InitContainers.Add(new ContainerAppInitContainer
            {
                Name = "migrations",
                Image = container.Image,
                Args = ["migrate"],
                Env = container.Env,
            });

            if (customDomain is not null)
            {
                app.ConfigureCustomDomain(customDomain, customDomainCertificate!);
            }
        });

    publicUrl = customDomain is not null
        ? ReferenceExpression.Create($"https://{customDomain}")
        : ReferenceExpression.Create($"{webApi.GetEndpoint("Public")}");
    daemonUrl = publicUrl;
    modelsUrl = ReferenceExpression.Create($"{publicUrl}/models");
    modes = [webApi];
}
else
{
    // Run here: Postgres in a container, checkpoints in a folder of this computer unless tests give
    // their own, and nooks on the Docker Engine, or in an Azure sandbox group too.
    IResourceBuilder<PostgresDatabaseResource> postgres = builder.AddPostgres("postgres")
        .WithImageTag("18")
        .AddDatabase("aisloth");
    database = postgres.Resource.ConnectionStringExpression;
    IResourceBuilder<ParameterResource> objectStorage = builder.AddParameter(
        "object-storage",
        builder.Configuration["Parameters:object-storage"]
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "aisloth", "objects"));

    // Nooks can also run in an Azure sandbox group, subscription/resource-group/group/region, signed in
    // with `az login`. They need the images in a public repository (nook-image-repository) and public
    // addresses for the daemon and model endpoints (nook-daemon-url, nook-models-url), such as a tunnel's.
    string? azureSandboxGroup = builder.Configuration["Parameters:azure-sandbox-group"];
    string? publicDaemonUrl = builder.Configuration["Parameters:nook-daemon-url"];
    string? publicModelsUrl = builder.Configuration["Parameters:nook-models-url"];
    string[]? azureGroup = azureSandboxGroup?.Split('/');
    if (azureGroup is not null && azureGroup.Length != 4)
    {
        throw new InvalidOperationException("azure-sandbox-group must be subscription/resource-group/group/region.");
    }

    // The images nooks start from: the base, and one per harness on top of it, pushed when there is a
    // repository for them. Docker's cache makes a rebuild without changes take seconds.
    IResourceBuilder<ExecutableResource> nookImageBuilt = BuildImage("nook-image", "nook", nookImage, after: null);
    IResourceBuilder<ExecutableResource>[] harnessImages = [.. harnesses.Select(harness => BuildImage("nook-image-" + harness, harness, HarnessImage(harness), nookImageBuilt))];
    IResourceBuilder<ExecutableResource>[] imagesReady = imageRepository is null
        ? [nookImageBuilt, .. harnessImages]
        : [PushImage("nook-image", nookImage, nookImageBuilt), .. harnesses.Select((harness, index) => PushImage("nook-image-" + harness, HarnessImage(harness), harnessImages[index]))];

    // Kestrel listens on the endpoints set here, and only there: Aspire's own URL variables would be
    // overridden by them anyway.
    IResourceBuilder<ProjectResource> webApi = builder.AddProject<Projects.Bagatka_AiSloth_WebApi>("webapi", options => options.ExcludeKestrelEndpoints = true)
        .WithHttpEndpoint(port: 5170, name: "Http")
        .WithEndpointsInEnvironment(_ => false)
        .WithHttpHealthCheck("/health", endpointName: "Http");
    EndpointReference httpEndpoint = webApi.GetEndpoint("Http");
    webApi.WithEnvironment("Kestrel__Endpoints__Http__Url", ReferenceExpression.Create($"http://localhost:{httpEndpoint.Property(EndpointProperty.TargetPort)}"));
    EndpointReference daemonEndpoint = NookFacing(webApi, "Daemon", builder.Configuration["DaemonPort"] ?? "5171");
    webApi.WithEnvironment("Kestrel__Endpoints__Daemon__Protocols", "Http2");
    EndpointReference modelsEndpoint = NookFacing(webApi, "Models", builder.Configuration["ModelsPort"] ?? "5172");

    // The WebApi in migration mode: applies every module's migrations, then exits.
    IResourceBuilder<ProjectResource> migrations = builder.AddProject<Projects.Bagatka_AiSloth_WebApi>("migrations", options => options.ExcludeKestrelEndpoints = true)
        .WithArgs("migrate")
        .WaitFor(postgres);
    webApi.WaitForCompletion(migrations);
    foreach (IResourceBuilder<ExecutableResource> image in imagesReady)
    {
        webApi.WaitForCompletion(image);
    }

    // Nooks reach the daemon and model endpoints at their public addresses, or through the Docker host.
    publicUrl = ReferenceExpression.Create($"{httpEndpoint}");
    daemonUrl = publicDaemonUrl is not null
        ? ReferenceExpression.Create($"{publicDaemonUrl}")
        : ReferenceExpression.Create($"http://host.docker.internal:{daemonEndpoint.Property(EndpointProperty.Port)}");
    modelsUrl = publicModelsUrl is not null
        ? ReferenceExpression.Create($"{publicModelsUrl.TrimEnd('/')}/models")
        : ReferenceExpression.Create($"http://host.docker.internal:{modelsEndpoint.Property(EndpointProperty.Port)}/models");
    modes = [webApi, migrations];
    foreach (IResourceBuilder<ProjectResource> mode in new[] { webApi, migrations })
    {
        mode.WithEnvironment("ObjectStorage__FileSystem__Root", objectStorage)
            .WithEnvironment("Sandboxing__Docker__Endpoint", dockerHost)
            .WithEnvironment("Sandboxing__Docker__Scope", sandboxScope);
        if (azureGroup is not null)
        {
            mode.WithEnvironment("Sandboxing__Azure__SubscriptionId", azureGroup[0])
                .WithEnvironment("Sandboxing__Azure__ResourceGroup", azureGroup[1])
                .WithEnvironment("Sandboxing__Azure__SandboxGroup", azureGroup[2])
                .WithEnvironment("Sandboxing__Azure__Region", azureGroup[3])
                .WithEnvironment("Sandboxing__Azure__Scope", sandboxScope);
        }
    }
}

// Both modes read the same settings (PATTERNS.md, entry 20).
foreach (IResourceBuilder<IResourceWithEnvironment> mode in modes)
{
    mode.WithEnvironment("Host__PublicUrl", publicUrl)
        .WithEnvironment("Database__ConnectionString", database)
        .WithEnvironment("Modules__Chats__ModelGatewayUrl", modelsUrl)
        .WithEnvironment("Encryption__Key", encryptionKey)
        .WithEnvironment("Modules__AgentAccounts__AllowChatGptPlans", allowChatGptPlans)
        .WithEnvironment("ModelGateway__AllowPrivateNetworks", modelPrivateNetworks)
        .WithEnvironment("Modules__Nooks__DaemonUrl", daemonUrl)
        .WithEnvironment("Modules__Nooks__Image", Published(nookImage))
        .WithEnvironment(environment =>
        {
            foreach (string harness in harnesses)
            {
                environment.EnvironmentVariables["Modules__Nooks__Images__" + harness] = Published(HarnessImage(harness));
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
        });
}

using DistributedApplication app = builder.Build();
app.Run();

string HarnessImage(string harness)
{
    return "aisloth-nook-" + harness + ":" + imageTag;
}

// Where nooks find an image: in the repository it is pushed to, or in this computer's Docker.
string Published(string image)
{
    return imageRepository is null ? image : imageRepository + "/" + image;
}

IResourceBuilder<ExecutableResource> BuildImage(string name, string target, string image, IResourceBuilder<ExecutableResource>? after)
{
    // Without a provenance attestation, which records each build, an unchanged rebuild is the same
    // image, so its tag never moves away from running nooks: Docker forgets a replaced image, and
    // can't snapshot nooks made from it.
    string[] tags = imageRepository is null ? ["--tag", image] : ["--tag", image, "--tag", Published(image)];
    IResourceBuilder<ExecutableResource> build = builder.AddExecutable(
            name, "docker", repositoryRoot, ["build", "--provenance=false", "--file", "src/Daemon/Dockerfile", "--target", target, .. tags, "."])
        .WithEnvironment("DOCKER_HOST", dockerHost);
    return after is null ? build : build.WaitForCompletion(after);
}

IResourceBuilder<ExecutableResource> PushImage(string name, string image, IResourceBuilder<ExecutableResource> build)
{
    return builder.AddExecutable(name + "-push", "docker", repositoryRoot, "push", "--quiet", Published(image))
        .WithEnvironment("DOCKER_HOST", dockerHost)
        .WaitForCompletion(build);
}

// Nooks are containers that reach these endpoints through the Docker host's gateway. On Linux the
// gateway isn't localhost, where Aspire's proxy and Kestrel would listen, so the WebApi listens on
// every IPv4 interface itself; Docker Desktop on WSL doesn't forward to dual-stack (`*`) listeners.
// Without the proxy a port is fixed, so tests pass free ones.
static EndpointReference NookFacing(IResourceBuilder<ProjectResource> webApi, string name, string port)
{
    webApi.WithEndpoint(name, endpoint =>
    {
        endpoint.UriScheme = "http";
        endpoint.IsProxied = false;
        endpoint.Port = int.Parse(port, CultureInfo.InvariantCulture);
        endpoint.TargetPort = endpoint.Port;
    });
    EndpointReference reference = webApi.GetEndpoint(name);
    webApi.WithEnvironment("Kestrel__Endpoints__" + name + "__Url", ReferenceExpression.Create($"http://0.0.0.0:{reference.Property(EndpointProperty.TargetPort)}"));
    return reference;
}
