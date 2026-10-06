using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Public addresses for the app's daemon (gRPC) and model endpoints, for nooks that can't reach this
/// computer: an ngrok agent in Docker, signed in with the token in an env file. Disposing it removes
/// the agent.
/// </summary>
internal sealed class NgrokTunnels : IAsyncDisposable
{
    private readonly string _container;
    private readonly string _configuration;

    private NgrokTunnels(string container, string configuration, Uri daemonUrl, Uri modelsUrl)
    {
        _container = container;
        _configuration = configuration;
        DaemonUrl = daemonUrl;
        ModelsUrl = modelsUrl;
    }

    public Uri DaemonUrl { get; }

    public Uri ModelsUrl { get; }

    /// <summary>Starts the agent, returning once both addresses are online; the token file is an env file with <c>NGROK_AUTHTOKEN</c>.</summary>
    public static async Task<NgrokTunnels> StartAsync(string tokenFile, int daemonPort, int modelsPort, CancellationToken ct)
    {
        int apiPort = ControlPlane.FreePort();
        string container = "aisloth-e2e-ngrok-" + RandomNumberGenerator.GetHexString(12, lowercase: true);

        // The agent runs as a user of its own; the configuration holds no secret.
        string configuration = Directory.CreateTempSubdirectory("aisloth-ngrok-").FullName;
        File.SetUnixFileMode(configuration, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        string file = Path.Combine(configuration, "ngrok.yml");
        await File.WriteAllTextAsync(file, string.Create(CultureInfo.InvariantCulture, $"""
            version: 3
            agent:
              web_addr: 127.0.0.1:{apiPort}
            endpoints:
              - name: daemon
                upstream:
                  url: http://127.0.0.1:{daemonPort}
                  protocol: http2
              - name: models
                upstream:
                  url: http://127.0.0.1:{modelsPort}
            """), ct);
        File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);

        await DockerAsync(["run", "--detach", "--name", container, "--network", "host", "--env-file", tokenFile, "--volume", configuration + ":/config:ro", "ngrok/ngrok:3", "start", "--all", "--config", "/config/ngrok.yml"], ct);
        Dictionary<string, Uri> endpoints = await EndpointsAsync(apiPort, ct);
        return new NgrokTunnels(container, configuration, endpoints["daemon"], endpoints["models"]);
    }

    public async ValueTask DisposeAsync()
    {
        await DockerAsync(["rm", "--force", _container], CancellationToken.None);
        Directory.Delete(_configuration, recursive: true);
    }

    // The agent's endpoints by name, once both are online, from its local API.
    private static async Task<Dictionary<string, Uri>> EndpointsAsync(int apiPort, CancellationToken ct)
    {
        using HttpClient api = new HttpClient { BaseAddress = new Uri(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{apiPort}/")) };
        using CancellationTokenSource patience = CancellationTokenSource.CreateLinkedTokenSource(ct);
        patience.CancelAfter(TimeSpan.FromMinutes(1));
        Dictionary<string, Uri> endpoints = new Dictionary<string, Uri>(StringComparer.Ordinal);
        while (endpoints.Count < 2)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), patience.Token);
            string listed;
            try
            {
                listed = await api.GetStringAsync(new Uri("api/endpoints", UriKind.Relative), patience.Token);
            }
            catch (HttpRequestException)
            {
                // The agent isn't listening yet.
                continue;
            }

            using JsonDocument document = JsonDocument.Parse(listed);
            foreach (JsonElement endpoint in document.RootElement.GetProperty("endpoints").EnumerateArray())
            {
                endpoints[endpoint.GetProperty("name").GetString()!] = new Uri(endpoint.GetProperty("url").GetString()!);
            }
        }

        return endpoints;
    }

    // The docker command against DOCKER_HOST's engine, as the AppHost uses it.
    private static async Task DockerAsync(IReadOnlyList<string> arguments, CancellationToken ct)
    {
        ProcessStartInfo start = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process docker = Process.Start(start)!;
        Task<string> output = docker.StandardOutput.ReadToEndAsync(ct);
        string error = await docker.StandardError.ReadToEndAsync(ct);
        await output;
        await docker.WaitForExitAsync(ct);
        if (docker.ExitCode != 0)
        {
            throw new InvalidOperationException("docker " + arguments[0] + " failed: " + error);
        }
    }
}
