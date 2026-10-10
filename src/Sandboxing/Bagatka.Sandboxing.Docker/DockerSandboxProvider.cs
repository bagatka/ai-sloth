using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
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

    private static readonly Error SandboxNotFound =
        Error.NotFound("sandboxing.sandbox_not_found", "The sandbox doesn't exist.");

    private static readonly Error NoSysbox = Error.Conflict(
        "sandboxing.sysbox_missing",
        "The Docker Engine has no " + DockerSandboxSettings.Runtime + " runtime, which sandboxes run under; install Sysbox (https://github.com/nestybox/sysbox) on its host.");

    private readonly SandboxNetworks _networks = new SandboxNetworks(
        docker, scope, new Dictionary<string, string>(StringComparer.Ordinal) { [ScopeLabel] = scope }, hostPorts);

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

        Result<string> created = await CreateContainerAsync(spec, image.Output, specHash, ct);
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

        await StartCreatedAsync(created.Output, ct);
        if (spec.Source.Value is SandboxImage fromImage)
        {
            await ImageTags.RetireOthersAsync(docker, fromImage.Reference, ct);
        }

        ContainerDetails started = await InspectRequiredAsync(created.Output, ct);
        return new Result<SandboxObservation>(Observe(started));
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

        await _networks.StopAsync(key, ct);
        return new Result<SandboxObservation>(Observe(container));
    }

    public async Task<Result<SandboxObservation>> ResumeAsync(SandboxKey key, CancellationToken ct)
    {
        ContainerDetails? container = await docker.InspectContainerAsync(ContainerName(key), ct);
        if (container is null)
        {
            return new Result<SandboxObservation>(SandboxNotFound);
        }

        // The network's router first, so the sandbox's network works once it runs.
        await _networks.StartAsync(key, ct);
        if (string.Equals(container.Status, "paused", StringComparison.Ordinal))
        {
            await docker.UnpauseContainerAsync(container.Id, ct);
            container = await InspectRequiredAsync(container.Id, ct);
        }
        else if (StateOf(container.Status, container.ExitCode) == SandboxState.Stopped)
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
        // The router and network go with their sandbox. Once begun, removing them finishes even if the
        // caller cancels: a router or network left without its sandbox is found by nothing that lists
        // sandboxes, so it would stay forever.
        // Not handled: a process killed while it creates or deletes a sandbox leaves its router and
        // network; a later create or delete of the same key removes them.
        ContainerDetails? container = await docker.InspectContainerAsync(ContainerName(key), ct);
        if (container is not null)
        {
            await docker.RemoveContainerAsync(container.Id, CancellationToken.None);
        }

        await _networks.RemoveAsync(key);
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
    // any other end is a failure. A created container is stopped too: its start never happened, as
    // when the engine's runtime had a passing fault, and nothing starts it but a resume.
    private static SandboxState StateOf(string status, int exitCode)
    {
        return status switch
        {
            "created" when exitCode == 0 => SandboxState.Stopped,
            "restarting" => SandboxState.Starting,
            "running" => SandboxState.Running,
            "paused" => SandboxState.Paused,
            "exited" when exitCode == 0 => SandboxState.Stopped,
            "removing" => SandboxState.Deleting,

            // "exited" or "created" otherwise, as a container the engine refused to start is left,
            // "dead", or a status this provider doesn't know: the entry point isn't running.
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
            SandboxNetworks.ExtraHosts,
            DockerSandboxSettings.Runtime,
            _networks.NameOf(spec.Key));
    }

    // The sandbox's network, then its container, created. One that stops before the container exists,
    // as when the control plane shuts down, removes the network again: nothing lists it without its
    // sandbox.
    private async Task<Result<string>> CreateContainerAsync(SandboxSpec spec, string image, string specHash, CancellationToken ct)
    {
        await _networks.CreateAsync(spec.Key, ct);
        try
        {
            return await docker.CreateContainerAsync(ContainerName(spec.Key), Configuration(spec, image, specHash), ct);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException)
        {
            await _networks.RemoveAsync(spec.Key);
            throw;
        }
    }

    // Starts a container just created. One that never started goes, so the next create makes a new
    // one: Sysbox can keep a failed start's registration and refuse every later start of the same
    // container.
    private async Task StartCreatedAsync(string container, CancellationToken ct)
    {
        try
        {
            await docker.StartContainerAsync(container, ct);
        }
        catch (HttpRequestException)
        {
            _ = await docker.RemoveContainerAsync(container, CancellationToken.None);
            throw;
        }
    }

    private string ContainerName(SandboxKey key)
    {
        return "bagatka-" + scope + "-" + Format(key.Value);
    }

    private string SnapshotReference(SnapshotKey snapshot)
    {
        return SnapshotRepository + ":" + Format(snapshot.Value);
    }
}
