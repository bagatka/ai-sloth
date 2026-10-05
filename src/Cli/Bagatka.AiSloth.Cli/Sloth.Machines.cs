using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Sandboxing;
using Bagatka.Sandboxing.Docker;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Cli;

// Machine mode: this computer runs a workspace's nooks in its Docker Engine for the control plane,
// which it dials on the daemon endpoint. It runs on Linux and macOS; on Windows, inside WSL.
internal sealed partial class Sloth
{
    private string MachinePath => Path.Combine(home, "machine.json");

    private async Task<int> ConnectMachineAsync(string url, string code, CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            await terminal.FailAsync("Machine mode runs on Linux and macOS. On Windows, run sloth inside WSL.");
            return 1;
        }

        bool parsed = Uri.TryCreate(url, UriKind.Absolute, out Uri? controlPlaneUrl);
        if (!parsed || controlPlaneUrl!.Scheme is not ("https" or "http"))
        {
            await terminal.FailAsync("The control plane's URL must be an absolute http or https URL.");
            return 2;
        }

        MachineCredential credential;
        try
        {
            credential = await MachineLink.RegisterAsync(controlPlaneUrl, code, ct);
        }
        catch (RpcException exception) when (exception.StatusCode == StatusCode.Unauthenticated)
        {
            await terminal.FailAsync("The code is unknown, used, or expired. Add the machine again for a new code.");
            return 1;
        }
        catch (RpcException exception)
        {
            await terminal.FailAsync("Couldn't reach the control plane at " + controlPlaneUrl + ": " + exception.Status.Detail);
            return 1;
        }

        await credential.WriteAsync(MachinePath, ct);
        await terminal.WriteLineAsync("Registered as machine " + credential.MachineId.ToString("D", CultureInfo.InvariantCulture) + ". Start it with: sloth machine run");
        return 0;
    }

    private async Task<int> RunMachineAsync(CancellationToken ct)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            await terminal.FailAsync("Machine mode runs on Linux and macOS. On Windows, run sloth inside WSL.");
            return 1;
        }

        MachineCredential? credential = await ReadMachineCredentialAsync(ct);
        if (credential is null)
        {
            return 2;
        }

        // Nooks run in the local Docker Engine, the one the docker command uses. The scope keeps this
        // machine's sandboxes apart from anything else on the engine.
        // Not handled: a DOCKER_HOST that isn't an address; the UriFormatException says so.
        Uri dockerEndpoint = new Uri(dockerHost ?? "unix:///var/run/docker.sock");
        DockerSandboxSettings docker;
        try
        {
            docker = new DockerSandboxSettings(dockerEndpoint, "m-" + credential.MachineId.ToString("N", CultureInfo.InvariantCulture));
        }
        catch (ArgumentException exception)
        {
            await terminal.FailAsync(exception.Message);
            return 2;
        }

        await using ServiceProvider services = new ServiceCollection().AddDockerSandboxProvider(docker).BuildServiceProvider();
        ISandboxProvider local = services.GetRequiredService<ISandboxProvider>();
        string? problem = await DockerProblemAsync(local, ct);
        if (problem is not null)
        {
            await terminal.FailAsync("Couldn't reach the Docker Engine at " + dockerEndpoint + ": " + problem);
            return 1;
        }

        using ILoggerFactory loggers = LoggerFactory.Create(logging => logging.AddSimpleConsole(console =>
        {
            console.SingleLine = true;
            console.UseUtcTimestamp = true;
            console.TimestampFormat = "yyyy-MM-dd'T'HH':'mm':'ss'.'fff'Z' ";
        }));
        MachineLink link = new MachineLink(credential, local, time, loggers.CreateLogger<MachineLink>());
        try
        {
            await link.RunAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Asked to stop; the sandboxes keep running and the control plane finds them on reconnect.
            return 0;
        }

        await terminal.FailAsync("The control plane no longer accepts this machine; it was removed from its workspace.");
        return 1;
    }

    // The credential `machine connect` kept, or null after saying why there's none.
    private async Task<MachineCredential?> ReadMachineCredentialAsync(CancellationToken ct)
    {
        MachineCredential? credential;
        try
        {
            credential = await MachineCredential.ReadAsync(MachinePath, ct);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException)
        {
            await terminal.FailAsync(MachinePath + " is damaged. Add the machine again and run sloth machine connect.");
            return null;
        }

        if (credential is null)
        {
            await terminal.FailAsync("This computer isn't registered yet. Run sloth machine connect with a code from the workspace.");
        }

        return credential;
    }

    // Listing proves the engine answers before the control plane sends calls that would fail.
    private static async Task<string?> DockerProblemAsync(ISandboxProvider local, CancellationToken ct)
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
}
