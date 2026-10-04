using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Cli;
using Bagatka.Sandboxing;
using Bagatka.Sandboxing.Docker;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// sloth's composition root: the only code that reads arguments, the environment, and files.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

const string Usage = """
    Usage:
      sloth machine connect <control-plane-url> <code>   Register this computer with a code from the web app
      sloth machine run                                  Run the workspace's nooks here, in Docker
    """;

using CancellationTokenSource shutdown = new CancellationTokenSource();
using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);

string credentialPath = Path.Combine(
    Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config ? config : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
    "sloth",
    "machine.json");

Task<int> command = args switch
{
    ["machine", "connect", string url, string code] => ConnectAsync(url, code),
    ["machine", "run"] => RunAsync(),
    _ => UsageAsync(),
};
return await command;

async Task<int> ConnectAsync(string url, string code)
{
    Uri? controlPlaneUrl = ParseUrl(url);
    if (controlPlaneUrl is null || controlPlaneUrl.Scheme is not ("https" or "http"))
    {
        await Console.Error.WriteLineAsync("sloth: the control plane URL must be an absolute http or https URL.");
        return 2;
    }

    MachineCredential credential;
    try
    {
        credential = await MachineLink.RegisterAsync(controlPlaneUrl, code, shutdown.Token);
    }
    catch (RpcException exception) when (exception.StatusCode == StatusCode.Unauthenticated)
    {
        await Console.Error.WriteLineAsync("sloth: the code is unknown, used, or expired. Add the machine again in the web app for a new code.");
        return 1;
    }
    catch (RpcException exception)
    {
        await Console.Error.WriteLineAsync("sloth: couldn't reach the control plane at " + controlPlaneUrl + ": " + exception.Status.Detail);
        return 1;
    }

    await credential.WriteAsync(credentialPath, shutdown.Token);
    await Console.Out.WriteLineAsync("Registered as machine " + credential.MachineId.ToString("D", CultureInfo.InvariantCulture) + ". Start it with: sloth machine run");
    return 0;
}

async Task<int> RunAsync()
{
    MachineCredential? credential;
    try
    {
        credential = await MachineCredential.ReadAsync(credentialPath, shutdown.Token);
    }
    catch (Exception exception) when (exception is JsonException or InvalidDataException)
    {
        await Console.Error.WriteLineAsync("sloth: " + credentialPath + " is damaged. Add the machine again in the web app and run sloth machine connect.");
        return 2;
    }

    if (credential is null)
    {
        await Console.Error.WriteLineAsync("sloth: this computer isn't registered yet. Run sloth machine connect with a code from the web app.");
        return 2;
    }

    // Nooks run in the local Docker Engine, the one the docker command uses. The scope keeps this
    // machine's sandboxes apart from anything else on the engine.
    Uri dockerEndpoint = new Uri(Environment.GetEnvironmentVariable("DOCKER_HOST") is { Length: > 0 } host ? host : "unix:///var/run/docker.sock");
    DockerSandboxSettings docker;
    try
    {
        docker = new DockerSandboxSettings(dockerEndpoint, "m-" + credential.MachineId.ToString("N", CultureInfo.InvariantCulture));
    }
    catch (ArgumentException exception)
    {
        await Console.Error.WriteLineAsync("sloth: " + exception.Message);
        return 2;
    }

    await using ServiceProvider services = new ServiceCollection().AddDockerSandboxProvider(docker).BuildServiceProvider();
    ISandboxProvider local = services.GetRequiredService<ISandboxProvider>();
    string? problem = await DockerProblemAsync(local, shutdown.Token);
    if (problem is not null)
    {
        await Console.Error.WriteLineAsync("sloth: couldn't reach the Docker Engine at " + dockerEndpoint + ": " + problem);
        return 1;
    }

    using ILoggerFactory loggers = LoggerFactory.Create(logging => logging.AddSimpleConsole(console =>
    {
        console.SingleLine = true;
        console.UseUtcTimestamp = true;
        console.TimestampFormat = "yyyy-MM-dd'T'HH':'mm':'ss'.'fff'Z' ";
    }));
    MachineLink link = new MachineLink(credential, local, TimeProvider.System, loggers.CreateLogger<MachineLink>());
    try
    {
        await link.RunAsync(shutdown.Token);
    }
    catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
    {
        // Asked to stop; the sandboxes keep running and the control plane finds them on reconnect.
        return 0;
    }

    await Console.Error.WriteLineAsync("sloth: the control plane no longer accepts this machine; it was removed from its workspace.");
    return 1;
}

static Uri? ParseUrl(string text)
{
    bool parsed = Uri.TryCreate(text, UriKind.Absolute, out Uri? url);
    return parsed ? url : null;
}

// Listing proves the engine answers before the control plane sends calls that would fail.
static async Task<string?> DockerProblemAsync(ISandboxProvider local, CancellationToken ct)
{
    try
    {
        await foreach (SandboxObservation _ in local.ListAsync(ct))
        {
            break;
        }

        return null;
    }
    catch (HttpRequestException exception)
    {
        return exception.Message;
    }
}

static async Task<int> UsageAsync()
{
    await Console.Error.WriteLineAsync(Usage);
    return 2;
}

void Stop(PosixSignalContext context)
{
    context.Cancel = true;
    shutdown.Cancel();
}
