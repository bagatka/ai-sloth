using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;
using Bagatka.Sdk.Docker;

namespace Bagatka.Sandboxing.Docker;

/// <summary>
/// Runs each sandbox as a container on a Docker Engine, under Sysbox, so that it can run containers of
/// its own without privileges on the host. Suspending stops the container, which frees its memory and
/// keeps its files, so a suspended sandbox is <see cref="SandboxState.Stopped"/>: a container whose
/// entry point exited cleanly, as it does when asked to stop. Snapshots are images committed from
/// containers. Each sandbox has a network and a router of its own, which let it reach the internet and
/// the host's ports in <c>hostPorts</c>, and nothing else (README, "Network").
/// </summary>
internal sealed class DockerSandboxProvider(DockerClient docker, string scope, IReadOnlyList<int> hostPorts) : ISandboxProvider
{
    private const string LabelPrefix = "com.bagatka.sandboxing.";
    private const string ScopeLabel = LabelPrefix + "scope";
    private const string KeyLabel = LabelPrefix + "key";
    private const string SpecLabel = LabelPrefix + "spec";
    private const string SnapshotLabel = LabelPrefix + "snapshot";
    private const string SourceLabel = LabelPrefix + "source";

    // Docker refuses containers with less memory than this.
    private const int MinimumMemoryMebibytes = 6;

    // How long a stopping sandbox's entry point has to end its processes before it is killed.
    private static readonly TimeSpan StopGrace = TimeSpan.FromSeconds(30);

    // Names the Docker host, whose ports in hostPorts sandboxes reach, such as a control plane's in
    // development.
    private static readonly IReadOnlyList<string> ExtraHosts = ["host.docker.internal:host-gateway"];

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

    private static readonly Error SandboxNotFound =
        Error.NotFound("sandboxing.sandbox_not_found", "The sandbox doesn't exist.");

    private static readonly Error NoSysbox = Error.Conflict(
        "sandboxing.sysbox_missing",
        "The Docker Engine has no " + DockerSandboxSettings.Runtime + " runtime, which sandboxes run under; install Sysbox (https://github.com/nestybox/sysbox) on its host.");

    private string ScopeFilter => ScopeLabel + "=" + scope;

    private string SnapshotRepository => "bagatka-snapshot-" + scope;

    public string Name => "docker";

    public async Task<Result<SandboxObservation>> CreateAsync(SandboxSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Location is not null)
        {
            return new Result<SandboxObservation>(Error.Validation("location", "A Docker Engine is one place; leave the location empty."));
        }

        // Docker reads zero CPU as unlimited and refuses tiny memory limits.
        if (spec.Resources.CpuMillicores <= 0 || spec.Resources.MemoryMebibytes < MinimumMemoryMebibytes)
        {
            return new Result<SandboxObservation>(Error.Validation(
                "resources",
                string.Create(CultureInfo.InvariantCulture, $"CPU must be positive and memory at least {MinimumMemoryMebibytes} MiB.")));
        }

        string name = ContainerName(spec.Key);
        string specHash = spec.Fingerprint();
        ContainerDetails? existing = await docker.InspectContainerAsync(name, ct);
        if (existing is not null)
        {
            return Repeated(existing, specHash);
        }

        IReadOnlyList<string> runtimes = await docker.ListRuntimesAsync(ct);
        if (!runtimes.Contains(DockerSandboxSettings.Runtime, StringComparer.Ordinal))
        {
            return new Result<SandboxObservation>(NoSysbox);
        }

        Result<string> image = await ResolveImageAsync(spec.Source, ct);
        if (image.Failed)
        {
            return new Result<SandboxObservation>(image.Error);
        }

        await CreateNetworkAsync(spec.Key, ct);
        await StartRouterAsync(spec.Key, ct);
        Result<string> created = await docker.CreateContainerAsync(name, Configuration(spec, image.Output, specHash), ct);
        if (created.Failed)
        {
            // A concurrent call may have created it first; answer as for a repeated call.
            bool nameTaken = created.Error.Kind == ErrorKind.Conflict;
            ContainerDetails? raced = null;
            if (nameTaken)
            {
                raced = await docker.InspectContainerAsync(name, ct);
            }

            return raced is null ? new Result<SandboxObservation>(created.Error) : Repeated(raced, specHash);
        }

        await docker.StartContainerAsync(created.Output, ct);
        if (spec.Source.Value is SandboxImage fromImage)
        {
            await RemoveOtherTagsAsync(fromImage.Reference, ct);
        }

        ContainerDetails started = await InspectRequiredAsync(created.Output, ct);
        return new Result<SandboxObservation>(Observe(started));
    }

    // A sandbox from one tag of an image retires the repository's other tags: each goes once no
    // container uses it, so an engine keeps one release of an image beside what still runs, instead of
    // every release it ever pulled. Images of other repositories are never touched.
    private async Task RemoveOtherTagsAsync(string reference, CancellationToken ct)
    {
        int tagAt = reference.LastIndexOf(':', StringComparison.Ordinal);
        bool tagged = tagAt > reference.LastIndexOf('/', StringComparison.Ordinal) && !reference.Contains('@', StringComparison.Ordinal);
        if (!tagged)
        {
            return;
        }

        string repository = reference[..(tagAt + 1)];
        IReadOnlyList<ImageListItem> images = await docker.ListImagesAsync([], ct);
        List<string> others = [.. images.SelectMany(image => image.RepoTags)
            .Where(tag => tag.StartsWith(repository, StringComparison.Ordinal) && !string.Equals(tag, reference, StringComparison.Ordinal))];
        foreach (string other in others)
        {
            // False when a container still uses it.
            _ = await docker.RemoveImageAsync(other, force: false, ct);
        }
    }

    public async Task<Result<SandboxObservation>> SuspendAsync(SandboxKey key, CancellationToken ct)
    {
        ContainerDetails? container = await docker.InspectContainerAsync(ContainerName(key), ct);
        if (container is null)
        {
            return new Result<SandboxObservation>(SandboxNotFound);
        }

        if (string.Equals(container.Status, "running", StringComparison.Ordinal))
        {
            await docker.StopContainerAsync(container.Id, StopGrace, ct);
            container = await InspectRequiredAsync(container.Id, ct);
        }

        await docker.StopContainerAsync(RouterName(key), RouterStopGrace, ct);
        return new Result<SandboxObservation>(Observe(container));
    }

    public async Task<Result<SandboxObservation>> ResumeAsync(SandboxKey key, CancellationToken ct)
    {
        ContainerDetails? container = await docker.InspectContainerAsync(ContainerName(key), ct);
        if (container is null)
        {
            return new Result<SandboxObservation>(SandboxNotFound);
        }

        // The router first, so the sandbox's network works once it runs.
        await docker.StartContainerAsync(RouterName(key), ct);
        if (string.Equals(container.Status, "paused", StringComparison.Ordinal))
        {
            await docker.UnpauseContainerAsync(container.Id, ct);
            container = await InspectRequiredAsync(container.Id, ct);
        }
        else if (StateOf(container.Status, container.ExitCode) is SandboxState.Starting or SandboxState.Stopped)
        {
            await docker.StartContainerAsync(container.Id, ct);
            container = await InspectRequiredAsync(container.Id, ct);
        }

        return new Result<SandboxObservation>(Observe(container));
    }

    public async Task<SandboxObservation?> ObserveAsync(SandboxKey key, CancellationToken ct)
    {
        ContainerDetails? container = await docker.InspectContainerAsync(ContainerName(key), ct);
        return container is null ? null : Observe(container);
    }

    public async IAsyncEnumerable<SandboxObservation> ListAsync([EnumeratorCancellation] CancellationToken ct)
    {
        // Sandboxes only, not their routers.
        IReadOnlyList<ContainerListItem> containers = await docker.ListContainersAsync([ScopeFilter, KeyLabel], ct);
        foreach (ContainerListItem container in containers)
        {
            // The list has no exit codes, which tell a stopped sandbox from a failed one.
            if (string.Equals(container.State, "exited", StringComparison.Ordinal))
            {
                ContainerDetails? exited = await docker.InspectContainerAsync(container.Id, ct);
                if (exited is not null && ParseKey(exited.Labels, KeyLabel) is not null)
                {
                    yield return Observe(exited);
                }
            }
            else if (ParseKey(container.Labels, KeyLabel) is Guid key)
            {
                SandboxState state = StateOf(container.State, exitCode: 0);
                string? reason = state == SandboxState.Failed ? container.Status : null;
                yield return new SandboxObservation(SandboxKey.From(key), state, container.Created, reason);
            }
        }
    }

    public async Task DeleteAsync(SandboxKey key, CancellationToken ct)
    {
        // The router and network go with their sandbox, also when creating it stopped halfway. Once
        // begun, removing them finishes even if the caller cancels: a router or network left without
        // its sandbox is found by nothing that lists sandboxes, so it would stay forever.
        // Not handled: creating a sandbox that stops halfway and is never deleted by its key, as when
        // its machine is removed meanwhile; its network and router stay.
        ContainerDetails? container = await docker.InspectContainerAsync(ContainerName(key), ct);
        if (container is not null)
        {
            await docker.RemoveContainerAsync(container.Id, CancellationToken.None);
        }

        _ = await docker.RemoveContainerAsync(RouterName(key), CancellationToken.None);
        _ = await docker.RemoveNetworkAsync(NetworkName(key), CancellationToken.None);
        if (container is null)
        {
            return;
        }

        // A deleted snapshot's image stays, untagged, while sandboxes created from it exist; the last
        // of them removes it. Without force, the removal succeeds only once no other sandbox uses it.
        ImageDetails? image = await docker.InspectImageAsync(container.ImageId, ct);
        if (image is { RepoTags.Count: 0 }
            && string.Equals(Label(image.Labels, ScopeLabel), scope, StringComparison.Ordinal)
            && Label(image.Labels, SnapshotLabel) is not null)
        {
            await docker.RemoveImageAsync(image.Id, force: false, ct);
        }
    }

    public async Task<Result<SnapshotObservation>> SnapshotAsync(SandboxKey sandbox, SnapshotKey snapshot, CancellationToken ct)
    {
        ImageListItem? existing = await FindSnapshotAsync(snapshot, ct);
        if (existing is not null)
        {
            return string.Equals(Label(existing.Labels, SourceLabel), Format(sandbox.Value), StringComparison.Ordinal)
                ? new Result<SnapshotObservation>(ObserveSnapshot(existing))
                : new Result<SnapshotObservation>(Error.Conflict("sandboxing.snapshot_key_in_use", "The snapshot key was used for another sandbox."));
        }

        ContainerDetails? container = await docker.InspectContainerAsync(ContainerName(sandbox), ct);
        if (container is null)
        {
            return new Result<SnapshotObservation>(SandboxNotFound);
        }

        // Not handled: a sandbox whose image a newer build or pull replaced, which Docker's containerd
        // image store then forgets, so it can't commit the sandbox either; handling it would take
        // pinning each sandbox's image with a tag of this provider's own.
        ImageDetails? image = await docker.InspectImageAsync(container.ImageId, ct);
        if (image is null)
        {
            throw new InvalidOperationException("The image of sandbox " + Format(sandbox.Value) + " is gone, as when a newer build or pull takes its tag, so Docker can't snapshot it.");
        }

        CommitConfiguration configuration = new CommitConfiguration(
            WithoutSandboxValues(container.Environment, image.Environment),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ScopeLabel] = scope,
                [SnapshotLabel] = Format(snapshot.Value),
                [SourceLabel] = Format(sandbox.Value),
            });

        await docker.CommitContainerAsync(container.Id, SnapshotRepository, Format(snapshot.Value), configuration, ct);
        ImageListItem? committed = await FindSnapshotAsync(snapshot, ct);
        if (committed is null)
        {
            throw new InvalidOperationException("Snapshot " + Format(snapshot.Value) + " was committed but isn't listed.");
        }

        return new Result<SnapshotObservation>(ObserveSnapshot(committed));
    }

    public async IAsyncEnumerable<SnapshotObservation> ListSnapshotsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        IReadOnlyList<ImageListItem> images = await docker.ListImagesAsync([ScopeFilter, SnapshotLabel], ct);
        foreach (ImageListItem image in images)
        {
            if (ParseKey(image.Labels, SnapshotLabel) is not null && ParseKey(image.Labels, SourceLabel) is not null)
            {
                yield return ObserveSnapshot(image);
            }
        }
    }

    public async Task DeleteSnapshotAsync(SnapshotKey snapshot, CancellationToken ct)
    {
        // Removing the tag deletes the image only when no container uses it, so sandboxes created
        // from the snapshot keep running; the last of them removes the image when it is deleted.
        await docker.RemoveImageAsync(SnapshotReference(snapshot), force: true, ct);
    }

    private static Result<SandboxObservation> Repeated(ContainerDetails container, string specHash)
    {
        return string.Equals(Label(container.Labels, SpecLabel), specHash, StringComparison.Ordinal)
            ? new Result<SandboxObservation>(Observe(container))
            : new Result<SandboxObservation>(Error.Conflict("sandboxing.key_in_use", "A sandbox with this key exists with a different spec."));
    }

    private static SandboxObservation Observe(ContainerDetails container)
    {
        Guid? key = ParseKey(container.Labels, KeyLabel);
        if (key is null)
        {
            throw new InvalidOperationException("Container " + container.Name + " has no sandbox key label.");
        }

        SandboxState state = StateOf(container.Status, container.ExitCode);
        string? reason = null;
        if (state == SandboxState.Failed)
        {
            reason = container.Error.Length > 0
                ? container.Error
                : string.Create(CultureInfo.InvariantCulture, $"Docker reports the container '{container.Status}' with exit code {container.ExitCode}.");
        }

        return new SandboxObservation(SandboxKey.From(key.Value), state, container.Created, reason);
    }

    private static SnapshotObservation ObserveSnapshot(ImageListItem image)
    {
        Guid? snapshot = ParseKey(image.Labels, SnapshotLabel);
        if (snapshot is null)
        {
            throw new InvalidOperationException("Image " + image.Id + " has no snapshot key label.");
        }

        Guid? source = ParseKey(image.Labels, SourceLabel);
        if (source is null)
        {
            throw new InvalidOperationException("Image " + image.Id + " has no source label.");
        }

        return new SnapshotObservation(SnapshotKey.From(snapshot.Value), SandboxKey.From(source.Value), image.Created);
    }

    // An entry point exits cleanly only when asked to stop, so a clean exit is a stopped sandbox;
    // any other end is a failure.
    private static SandboxState StateOf(string status, int exitCode)
    {
        return status switch
        {
            "created" or "restarting" => SandboxState.Starting,
            "running" => SandboxState.Running,
            "paused" => SandboxState.Paused,
            "exited" when exitCode == 0 => SandboxState.Stopped,
            "removing" => SandboxState.Deleting,

            // "exited" otherwise, "dead", or a status this provider doesn't know: the entry point isn't running.
            _ => SandboxState.Failed,
        };
    }

    // A commit adds every container variable missing from its own list, which would copy the
    // sandbox's secrets into the snapshot. The image's own variables, plus the sandbox-only names
    // with empty values, keep them out; a sandbox created from the snapshot sets its own values.
    private static List<string> WithoutSandboxValues(IReadOnlyList<string> sandboxEnvironment, IReadOnlyList<string> imageEnvironment)
    {
        HashSet<string> imageNames = imageEnvironment.Select(VariableName).ToHashSet(StringComparer.Ordinal);
        List<string> environment = [.. imageEnvironment];
        environment.AddRange(sandboxEnvironment
            .Select(VariableName)
            .Where(name => !imageNames.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .Select(name => name + "="));
        return environment;
    }

    private static string VariableName(string variable)
    {
        int equals = variable.IndexOf('=', StringComparison.Ordinal);
        return equals < 0 ? variable : variable[..equals];
    }

    private static string? Label(IReadOnlyDictionary<string, string> labels, string name)
    {
        return labels.GetValueOrDefault(name);
    }

    private static Guid? ParseKey(IReadOnlyDictionary<string, string> labels, string name)
    {
        bool parsed = Guid.TryParseExact(Label(labels, name), "N", out Guid key);
        return parsed ? key : null;
    }

    private static string Format(Guid value)
    {
        return value.ToString("N", CultureInfo.InvariantCulture);
    }

    private async Task<Result<string>> ResolveImageAsync(SandboxSource source, CancellationToken ct)
    {
        switch (source.Value)
        {
            case SandboxImage image:
                ImageDetails? pulled = await docker.InspectImageAsync(image.Reference, ct);
                if (pulled is null)
                {
                    await docker.PullImageAsync(image.Reference, ct);
                }

                return new Result<string>(image.Reference);
            case SnapshotKey snapshot:
                ImageListItem? saved = await FindSnapshotAsync(snapshot, ct);
                return saved is null
                    ? new Result<string>(Error.NotFound("sandboxing.snapshot_not_found", "The snapshot doesn't exist."))
                    : new Result<string>(SnapshotReference(snapshot));
            default:
                throw new InvalidOperationException("The sandbox source is default.");
        }
    }

    private async Task<ImageListItem?> FindSnapshotAsync(SnapshotKey snapshot, CancellationToken ct)
    {
        IReadOnlyList<ImageListItem> images = await docker.ListImagesAsync([ScopeFilter, SnapshotLabel + "=" + Format(snapshot.Value)], ct);
        return images.Count > 0 ? images[0] : null;
    }

    private async Task<ContainerDetails> InspectRequiredAsync(string id, CancellationToken ct)
    {
        // Not handled: another caller deleting the sandbox between two calls of one operation;
        // answering not found instead would need every caller of this method to return a result.
        ContainerDetails? container = await docker.InspectContainerAsync(id, ct);
        if (container is null)
        {
            throw new InvalidOperationException("Container " + id + " disappeared while being changed.");
        }

        return container;
    }

    private ContainerConfiguration Configuration(SandboxSpec spec, string image, string specHash)
    {
        return new ContainerConfiguration(
            image,
            spec.Environment.Select(variable => variable.Key + "=" + variable.Value).ToList(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ScopeLabel] = scope,
                [KeyLabel] = Format(spec.Key.Value),
                [SpecLabel] = specHash,
            },
            NanoCpus: spec.Resources.CpuMillicores * 1_000_000L,
            MemoryBytes: spec.Resources.MemoryMebibytes * 1024L * 1024L,
            ExtraHosts,
            DockerSandboxSettings.Runtime,
            NetworkName(spec.Key));
    }

    // The sandbox's network, on a subnet no other network uses; one left by an earlier attempt to
    // create the sandbox does.
    private async Task CreateNetworkAsync(SandboxKey key, CancellationToken ct)
    {
        Dictionary<string, string> labels = new Dictionary<string, string>(StringComparer.Ordinal) { [ScopeLabel] = scope };
        int first = RandomNumberGenerator.GetInt32(Subnets);
        for (int attempt = 0; attempt < SubnetAttempts; attempt++)
        {
            int offset = (first + attempt) % Subnets * 8;
            string subnet = string.Create(CultureInfo.InvariantCulture, $"198.{18 + (offset >> 16)}.{(offset >> 8) & 255}.{offset & 255}/29");
            Result created = await docker.CreateNetworkAsync(NetworkName(key), subnet, NetworkOptions, labels, ct);
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
        throw new InvalidOperationException("No subnet of 198.18.0.0/15 was free for sandbox " + Format(key.Value) + "'s network.");
    }

    // The sandbox's router, started before the sandbox, so its rules are in place when the sandbox's
    // first packet arrives; until then, the sandbox's packets go nowhere. One left by an earlier
    // attempt to create the sandbox is started as it is.
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
        return new ContainerConfiguration(
            RouterImage,
            environment,
            new Dictionary<string, string>(StringComparer.Ordinal) { [ScopeLabel] = scope },
            RouterNanoCpus,
            RouterMemoryBytes,
            ExtraHosts,
            RouterRuntime,
            Uplink)
        {
            Command = ["/bin/sh", "-c", RouterScript],
            Capabilities = ["NET_ADMIN"],
            Sysctls = new Dictionary<string, string>(StringComparer.Ordinal) { ["net.ipv4.ip_forward"] = "1" },
            ExtraNetworks = [NetworkName(key)],
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

        await RemoveOtherTagsAsync(RouterImage, ct);
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
        using Stream? stream = typeof(DockerSandboxProvider).Assembly.GetManifestResourceStream("router.sh");
        if (stream is null)
        {
            throw new InvalidOperationException("The script router.sh isn't embedded in the Docker sandbox provider.");
        }

        using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    private string ContainerName(SandboxKey key)
    {
        return "bagatka-" + scope + "-" + Format(key.Value);
    }

    private string NetworkName(SandboxKey key)
    {
        return ContainerName(key);
    }

    private string RouterName(SandboxKey key)
    {
        return ContainerName(key) + "-router";
    }

    private string SnapshotReference(SnapshotKey snapshot)
    {
        return SnapshotRepository + ":" + Format(snapshot.Value);
    }
}
