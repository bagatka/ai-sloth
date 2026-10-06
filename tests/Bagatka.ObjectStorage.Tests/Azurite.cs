using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Xunit;

[assembly: AssemblyFixture(typeof(Bagatka.ObjectStorage.Tests.Azurite))]

namespace Bagatka.ObjectStorage.Tests;

/// <summary>
/// Azure's Blob Storage emulator in a container on DOCKER_HOST's engine, as the docker command finds
/// it, for the tests of the Azure Blob backend. Disposing it removes the container.
/// </summary>
public sealed class Azurite : IAsyncLifetime
{
    private readonly string _container = "bagatka-azurite-" + RandomNumberGenerator.GetHexString(12, lowercase: true);
    private readonly int _port = FreePort();

    /// <summary>The emulator's account, with the development key Azure's documentation publishes for it.</summary>
    public string ConnectionString => string.Create(
        CultureInfo.InvariantCulture,
        $"DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:{_port}/devstoreaccount1;");

    public async ValueTask InitializeAsync()
    {
        await DockerAsync(["run", "--detach", "--name", _container, "--publish", string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{_port}:10000"), "mcr.microsoft.com/azure-storage/azurite", "azurite-blob", "--blobHost", "0.0.0.0", "--skipApiVersionCheck"]);

        // Ready once it accepts connections.
        long started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            try
            {
                using TcpClient client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, _port, TestContext.Current.CancellationToken);
                return;
            }
            catch (SocketException) when (TimeProvider.System.GetElapsedTime(started) < TimeSpan.FromSeconds(60))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), TestContext.Current.CancellationToken);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await DockerAsync(["rm", "--force", _container]);
    }

    private static int FreePort()
    {
        using TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task DockerAsync(IReadOnlyList<string> arguments)
    {
        ProcessStartInfo start = new ProcessStartInfo("docker") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process docker = Process.Start(start)!;
        Task<string> output = docker.StandardOutput.ReadToEndAsync();
        string error = await docker.StandardError.ReadToEndAsync();
        await output;
        await docker.WaitForExitAsync();
        if (docker.ExitCode != 0)
        {
            throw new InvalidOperationException("docker " + arguments[0] + " failed: " + error);
        }
    }
}
