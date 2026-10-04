using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;
using Bagatka.Sdk.Docker;

namespace Bagatka.Sandboxing.Docker;

/// <summary>
/// Runs each sandbox as a container on a Docker Engine. Suspending pauses the container, which keeps
/// its memory, so a suspended sandbox is <see cref="SandboxState.Paused"/>. Snapshots are images
/// committed from containers.
/// </summary>
internal sealed class DockerSandboxProvider(DockerClient docker, string scope) : ISandboxProvider
{
    private const string LabelPrefix = "com.bagatka.sandboxing.";
    private const string ScopeLabel = LabelPrefix + "scope";
    private const string KeyLabel = LabelPrefix + "key";
    private const string SpecLabel = LabelPrefix + "spec";
    private const string SnapshotLabel = LabelPrefix + "snapshot";
    private const string SourceLabel = LabelPrefix + "source";

    // Docker refuses containers with less memory than this.
    private const int MinimumMemoryMebibytes = 6;

    // Lets a sandbox reach services on the Docker host, such as a control plane in development.
    private static readonly IReadOnlyList<string> ExtraHosts = ["host.docker.internal:host-gateway"];

    private static readonly Error SandboxNotFound =
        Error.NotFound("sandboxing.sandbox_not_found", "The sandbox doesn't exist.");

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
        string specHash = Hash(spec);
        ContainerDetails? existing = await docker.InspectContainerAsync(name, ct);
        if (existing is not null)
        {
            return Repeated(existing, specHash);
        }

        Result<string> image = await ResolveImageAsync(spec.Source, ct);
        if (image.Failed)
        {
            return new Result<SandboxObservation>(image.Error);
        }

        ContainerConfiguration configuration = new ContainerConfiguration(
            image.Output,
            spec.Environment.Select(variable => variable.Key + "=" + variable.Value).ToList(),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ScopeLabel] = scope,
                [KeyLabel] = Format(spec.Key.Value),
                [SpecLabel] = specHash,
            },
            NanoCpus: spec.Resources.CpuMillicores * 1_000_000L,
            MemoryBytes: spec.Resources.MemoryMebibytes * 1024L * 1024L,
            ExtraHosts);

        Result<string> created = await docker.CreateContainerAsync(name, configuration, ct);
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
            await docker.PauseContainerAsync(container.Id, ct);
            container = await InspectRequiredAsync(container.Id, ct);
        }

        return new Result<SandboxObservation>(Observe(container));
    }

    public async Task<Result<SandboxObservation>> ResumeAsync(SandboxKey key, CancellationToken ct)
    {
        ContainerDetails? container = await docker.InspectContainerAsync(ContainerName(key), ct);
        if (container is null)
        {
            return new Result<SandboxObservation>(SandboxNotFound);
        }

        if (string.Equals(container.Status, "paused", StringComparison.Ordinal))
        {
            await docker.UnpauseContainerAsync(container.Id, ct);
            container = await InspectRequiredAsync(container.Id, ct);
        }
        else if (string.Equals(container.Status, "created", StringComparison.Ordinal))
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
        IReadOnlyList<ContainerListItem> containers = await docker.ListContainersAsync([ScopeFilter], ct);
        foreach (ContainerListItem container in containers)
        {
            if (ParseKey(container.Labels, KeyLabel) is Guid key)
            {
                SandboxState state = StateOf(container.State);
                string? reason = state == SandboxState.Failed ? container.Status : null;
                yield return new SandboxObservation(SandboxKey.From(key), state, container.Created, reason);
            }
        }
    }

    public async Task DeleteAsync(SandboxKey key, CancellationToken ct)
    {
        ContainerDetails? container = await docker.InspectContainerAsync(ContainerName(key), ct);
        if (container is null)
        {
            return;
        }

        await docker.RemoveContainerAsync(container.Id, ct);

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

        ImageDetails? image = await docker.InspectImageAsync(container.ImageId, ct);
        if (image is null)
        {
            throw new InvalidOperationException("The image of sandbox " + Format(sandbox.Value) + " is missing.");
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

        SandboxState state = StateOf(container.Status);
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

    private static SandboxState StateOf(string status)
    {
        return status switch
        {
            "created" or "restarting" => SandboxState.Starting,
            "running" => SandboxState.Running,
            "paused" => SandboxState.Paused,
            "removing" => SandboxState.Deleting,

            // "exited", "dead", or a status this provider doesn't know: the entry point isn't running.
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

    // Identifies a spec so that a repeated create can tell "same sandbox" from "different spec".
    private static string Hash(SandboxSpec spec)
    {
        StringBuilder canonical = new StringBuilder();
        canonical.Append(spec.Source.Value switch
        {
            SandboxImage image => "image:" + image.Reference,
            SnapshotKey snapshot => "snapshot:" + Format(snapshot.Value),
            _ => throw new InvalidOperationException("The sandbox source is default."),
        });
        canonical.Append('\n').Append(spec.Resources.CpuMillicores.ToString(CultureInfo.InvariantCulture));
        canonical.Append('\n').Append(spec.Resources.MemoryMebibytes.ToString(CultureInfo.InvariantCulture));
        foreach (KeyValuePair<string, string> variable in spec.Environment.OrderBy(variable => variable.Key, StringComparer.Ordinal))
        {
            canonical.Append('\n').Append(variable.Key).Append('=').Append(variable.Value);
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
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

    private string ContainerName(SandboxKey key)
    {
        return "bagatka-" + scope + "-" + Format(key.Value);
    }

    private string SnapshotReference(SnapshotKey snapshot)
    {
        return SnapshotRepository + ":" + Format(snapshot.Value);
    }
}
