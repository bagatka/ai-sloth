using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;
using Bagatka.Sdk.Docker;

namespace Bagatka.Sandboxing.Docker;

// Each sandbox's own network, and the router that is its only way out (README, "Network"). The host
// takes no part in the network; the router, a small Alpine container on the engine's default bridge,
// takes the network's gateway address and forwards the sandbox's traffic to the internet and to the
// host's ports in hostPorts, and nowhere else. Both are named after the sandbox and go with it.
internal sealed class SandboxNetworks(DockerClient docker, string scope, IReadOnlyDictionary<string, string> labels, IReadOnlyList<int> hostPorts)
{
    // Names the Docker host, as sandboxes and routers know it.
    public static readonly IReadOnlyList<string> ExtraHosts = ["host.docker.internal:host-gateway"];

    // Routers run Alpine with iptables, an image each engine builds once (BuildRouterImageAsync). A
    // new tag makes engines build it again, when what it installs changes.
    private const string RouterBase = "alpine:3.22";
    private const string RouterRepository = "bagatka-router";
    private const string RouterTag = "1";
    private const string RouterImage = RouterRepository + ":" + RouterTag;
    private const string RouterRuntime = "runc";
    private const long RouterNanoCpus = 250_000_000;
    private const long RouterMemoryBytes = 32L * 1024 * 1024;
    private static readonly TimeSpan RouterStopGrace = TimeSpan.FromSeconds(5);
    private static readonly string RouterScript = ReadRouterScript();

    // The network routers reach the internet through: the engine's default bridge.
    private const string Uplink = "bridge";

    // Every sandbox's network is a /29 of 198.18.0.0/15, a range reserved for benchmarks that no real
    // network uses: 16,384 of them, tried from a random one on.
    private const int Subnets = 16_384;
    private const int SubnetAttempts = 32;

    // The host takes no part in a sandbox's network: it has no address there, so nothing but the
    // router leads anywhere; it doesn't masquerade the network's traffic; and the bridge's MTU is
    // below IPv6's minimum of 1280, so the host has no IPv6 there either, not even link-local, which a
    // sandbox's root could otherwise reach the host's services at.
    private static readonly IReadOnlyDictionary<string, string> NetworkOptions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["com.docker.network.bridge.inhibit_ipv4"] = "true",
        ["com.docker.network.bridge.enable_ip_masquerade"] = "false",
        ["com.docker.network.driver.mtu"] = "1279",
    };

    // The network the sandbox's container joins.
    public string NameOf(SandboxKey key)
    {
        return "bagatka-" + scope + "-" + key.Value.ToString("N", CultureInfo.InvariantCulture);
    }

    // Creates the sandbox's network and starts its router, before the sandbox exists, so the rules are
    // in place when its first packet arrives; until then, its packets go nowhere. What an earlier
    // attempt left is used as it is. One that stops halfway removes both again: nothing lists them
    // without their sandbox.
    public async Task CreateAsync(SandboxKey key, CancellationToken ct)
    {
        try
        {
            await CreateNetworkAsync(key, ct);
            await StartRouterAsync(key, ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or OperationCanceledException)
        {
            await RemoveAsync(key);
            throw;
        }
    }

    // Starts the router of a sandbox about to start again; starting a running one does nothing.
    public Task StartAsync(SandboxKey key, CancellationToken ct)
    {
        return docker.StartContainerAsync(RouterName(key), ct);
    }

    // Stops the router of a stopped sandbox, which needs no network until it starts again.
    public Task StopAsync(SandboxKey key, CancellationToken ct)
    {
        return docker.StopContainerAsync(RouterName(key), RouterStopGrace, ct);
    }

    // Removes the router and network, once their sandbox is gone or never came. Once begun, this
    // finishes even if the caller cancels.
    // Not handled: a process killed while it creates or deletes a sandbox leaves its router and network;
    // a later create or delete of the same sandbox removes them.
    public async Task RemoveAsync(SandboxKey key)
    {
        _ = await docker.RemoveContainerAsync(RouterName(key), CancellationToken.None);
        _ = await docker.RemoveNetworkAsync(NameOf(key), CancellationToken.None);
    }

    // The network, on a subnet no other network uses.
    private async Task CreateNetworkAsync(SandboxKey key, CancellationToken ct)
    {
        int first = RandomNumberGenerator.GetInt32(Subnets);
        for (int attempt = 0; attempt < SubnetAttempts; attempt++)
        {
            int offset = (first + attempt) % Subnets * 8;
            string subnet = string.Create(CultureInfo.InvariantCulture, $"198.{18 + (offset >> 16)}.{(offset >> 8) & 255}.{offset & 255}/29");
            Result created = await docker.CreateNetworkAsync(NameOf(key), subnet, NetworkOptions, labels, ct);
            if (!created.Failed || created.Error.Code is "docker.network_exists")
            {
                return;
            }

            if (created.Error.Code is not "docker.subnet_in_use")
            {
                throw new InvalidOperationException(created.Error.Message);
            }
        }

        // Not handled: an engine with thousands of networks in 198.18.0.0/15.
        throw new InvalidOperationException("No subnet of 198.18.0.0/15 was free for the network " + NameOf(key) + ".");
    }

    private async Task StartRouterAsync(SandboxKey key, CancellationToken ct)
    {
        await BuildRouterImageAsync(ct);
        Result<string> created = await docker.CreateContainerAsync(RouterName(key), RouterConfiguration(key), ct);
        if (created.Failed && created.Error.Kind != ErrorKind.Conflict)
        {
            throw new InvalidOperationException(created.Error.Message);
        }

        await docker.StartContainerAsync(RouterName(key), ct);
    }

    private ContainerConfiguration RouterConfiguration(SandboxKey key)
    {
        List<string> environment =
        [
            "HOST_PORTS=" + string.Join(' ', hostPorts.Select(port => port.ToString(CultureInfo.InvariantCulture))),
            "BLOCKED=" + string.Join(' ', OwnAddresses()),
        ];
        return new ContainerConfiguration(RouterImage, environment, labels, RouterNanoCpus, RouterMemoryBytes, ExtraHosts, RouterRuntime, Uplink)
        {
            Command = ["/bin/sh", "-c", RouterScript],
            Capabilities = ["NET_ADMIN"],
            Sysctls = new Dictionary<string, string>(StringComparer.Ordinal) { ["net.ipv4.ip_forward"] = "1" },
            ExtraNetworks = [NameOf(key)],
        };
    }

    // The routers' image, built once per engine by committing a container that installed iptables on
    // Alpine, which takes the internet that once. Another tag's image goes once no router uses it.
    private async Task BuildRouterImageAsync(CancellationToken ct)
    {
        ImageDetails? built = await docker.InspectImageAsync(RouterImage, ct);
        if (built is not null)
        {
            return;
        }

        ImageDetails? alpine = await docker.InspectImageAsync(RouterBase, ct);
        if (alpine is null)
        {
            await docker.PullImageAsync(RouterBase, ct);
        }

        ContainerConfiguration install = new ContainerConfiguration(
            RouterBase,
            [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            NanoCpus: 1_000_000_000,
            MemoryBytes: 256L * 1024 * 1024,
            [],
            RouterRuntime,
            Uplink)
        {
            Command = ["apk", "add", "--no-cache", "iptables"],
        };

        // Concurrent builds each use a container of their own and commit the same image.
        string name = RouterRepository + "-build-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        Result<string> created = await docker.CreateContainerAsync(name, install, ct);
        if (created.Failed)
        {
            throw new InvalidOperationException(created.Error.Message);
        }

        try
        {
            await docker.StartContainerAsync(created.Output, ct);
            int exitCode = await docker.WaitContainerAsync(created.Output, ct);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Installing iptables in {RouterBase} for sandboxes' routers exited with {exitCode}; building {RouterImage} needs the internet once."));
            }

            await docker.CommitContainerAsync(created.Output, RouterRepository, RouterTag, new CommitConfiguration([], new Dictionary<string, string>(StringComparer.Ordinal)), ct);
        }
        finally
        {
            await docker.RemoveContainerAsync(created.Output, CancellationToken.None);
        }

        await ImageTags.RetireOthersAsync(docker, RouterImage, ct);
    }

    // This computer's IPv4 addresses, which routers keep sandboxes from, as on a server whose public
    // address is its own: the provider reaches its engine over a Unix socket, so it runs beside it.
    private static IEnumerable<string> OwnAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(unicast => unicast.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            .Select(address => address.ToString());
    }

    private static string ReadRouterScript()
    {
        using Stream? stream = typeof(SandboxNetworks).Assembly.GetManifestResourceStream("router.sh");
        if (stream is null)
        {
            throw new InvalidOperationException("The script router.sh isn't embedded in the Docker sandbox provider.");
        }

        using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private string RouterName(SandboxKey key)
    {
        return NameOf(key) + "-router";
    }
}
