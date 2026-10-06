using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Bagatka.Azure.Sandboxes;
using Bagatka.Azure.Sandboxes.Models;
using Bagatka.Foundation;
using AzureResources = Bagatka.Azure.Sandboxes.Models.SandboxResources;
using AzureSandbox = Bagatka.Azure.Sandboxes.Models.Sandbox;
using AzureSource = Bagatka.Azure.Sandboxes.Models.SandboxSource;
using AzureState = Bagatka.Azure.Sandboxes.Models.SandboxState;

namespace Bagatka.Sandboxing.Azure;

/// <summary>
/// Runs each sandbox as a microVM in an Azure Container Apps sandbox group. Suspending stops it with
/// its memory (<see cref="SandboxState.Paused"/>), and snapshots are committed disk images. The
/// service chooses IDs, so every resource is found by its labels.
/// </summary>
internal sealed class AzureSandboxProvider(SandboxGroupClient client, string scope, TimeProvider time) : ISandboxProvider
{
    private const string ScopeLabel = "sandboxing-scope";
    private const string KeyLabel = "sandboxing-key";
    private const string SpecLabel = "sandboxing-spec";
    private const string SnapshotLabel = "sandboxing-snapshot";
    private const string SourceLabel = "sandboxing-source";
    private const string ImageLabel = "sandboxing-image";

    // Labels hold at most 63 characters; half the fingerprint tells specs apart as well.
    private const int SpecLabelLength = 32;

    // The largest disk the service allows: 20 GiB per core of CPU.
    private const int DiskMebibytesPerCore = 20 * 1024;

    // A safety net for a control plane that is down: the service suspends a sandbox idle this long.
    private static readonly TimeSpan SafetySuspend = TimeSpan.FromMinutes(30);

    // How long an image's disk image is used before it is made again, so a tag that moves reaches new
    // sandboxes within a day.
    private static readonly TimeSpan ImageFreshFor = TimeSpan.FromDays(1);

    private static readonly Error SandboxNotFound =
        Error.NotFound("sandboxing.sandbox_not_found", "The sandbox doesn't exist.");

    public string Name => "azure";

    public async Task<Result<SandboxObservation>> CreateAsync(SandboxSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.Location is not null)
        {
            return new Result<SandboxObservation>(Error.Validation("location", "A sandbox group is one place; leave the location empty."));
        }

        if (spec.Resources.CpuMillicores <= 0 || spec.Resources.MemoryMebibytes <= 0)
        {
            return new Result<SandboxObservation>(Error.Validation("resources", "CPU and memory must be positive."));
        }

        string specLabel = spec.Fingerprint()[..SpecLabelLength];
        AzureSandbox? existing = await FindAsync(spec.Key, ct);
        if (existing is not null)
        {
            return Repeated(existing, specLabel);
        }

        // Not handled: concurrent or retried creates of one key leave two sandboxes; the first is used,
        // and deleting the key deletes both.
        Task<Result<SandboxObservation>> creating = spec.Source.Value switch
        {
            SandboxImage image => CreateFromImageAsync(spec, image, specLabel, ct),
            SnapshotKey snapshot => CreateFromSnapshotAsync(spec, snapshot, specLabel, ct),
            _ => throw new InvalidOperationException("The sandbox source is default."),
        };
        return await creating;
    }

    public async Task<Result<SandboxObservation>> SuspendAsync(SandboxKey key, CancellationToken ct)
    {
        AzureSandbox? sandbox = await FindAsync(key, ct);
        if (sandbox is null)
        {
            return new Result<SandboxObservation>(SandboxNotFound);
        }

        if (sandbox.State == AzureState.Running || sandbox.State == AzureState.Idle)
        {
            Operation<AzureSandbox> stopped = await client.StopSandboxAsync(WaitUntil.Completed, sandbox.Id, ct);
            sandbox = stopped.Value;
        }

        // Not handled: a sandbox still being created or stopped; suspending it once it settles works.
        if (sandbox.State != AzureState.Stopped)
        {
            return new Result<SandboxObservation>(Error.Conflict("sandboxing.sandbox_busy", "The sandbox is " + sandbox.State + "; suspend it once it settles."));
        }

        return new Result<SandboxObservation>(Observe(sandbox));
    }

    public async Task<Result<SandboxObservation>> ResumeAsync(SandboxKey key, CancellationToken ct)
    {
        AzureSandbox? sandbox = await FindAsync(key, ct);
        if (sandbox is null)
        {
            return new Result<SandboxObservation>(SandboxNotFound);
        }

        if (sandbox.State == AzureState.Stopped)
        {
            Operation<AzureSandbox> resumed = await client.ResumeSandboxAsync(WaitUntil.Completed, sandbox.Id, ct);
            sandbox = resumed.Value;
        }

        return new Result<SandboxObservation>(Observe(sandbox));
    }

    public async Task<SandboxObservation?> ObserveAsync(SandboxKey key, CancellationToken ct)
    {
        AzureSandbox? sandbox = await FindAsync(key, ct);
        return sandbox is null ? null : Observe(sandbox);
    }

    public async IAsyncEnumerable<SandboxObservation> ListAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (AzureSandbox sandbox in client.GetSandboxesAsync(Labels(), ct))
        {
            if (ParseKey(sandbox.Labels, KeyLabel) is not null)
            {
                yield return Observe(sandbox);
            }
        }
    }

    public async Task DeleteAsync(SandboxKey key, CancellationToken ct)
    {
        await foreach (AzureSandbox sandbox in client.GetSandboxesAsync(Labels((KeyLabel, Format(key.Value))), ct))
        {
            await client.DeleteSandboxAsync(sandbox.Id, ct);
        }
    }

    public async Task<Result<SnapshotObservation>> SnapshotAsync(SandboxKey sandbox, SnapshotKey snapshot, CancellationToken ct)
    {
        DiskImage? existing = await FindSnapshotAsync(snapshot, ct);
        if (existing is not null)
        {
            return string.Equals(existing.Labels.GetValueOrDefault(SourceLabel), Format(sandbox.Value), StringComparison.Ordinal)
                ? new Result<SnapshotObservation>(ObserveSnapshot(existing))
                : new Result<SnapshotObservation>(Error.Conflict("sandboxing.snapshot_key_in_use", "The snapshot key was used for another sandbox."));
        }

        AzureSandbox? source = await FindAsync(sandbox, ct);
        if (source is null)
        {
            return new Result<SnapshotObservation>(SandboxNotFound);
        }

        // The service commits running sandboxes only, so a suspended one wakes briefly.
        bool suspended = source.State == AzureState.Stopped;
        if (suspended)
        {
            await client.ResumeSandboxAsync(WaitUntil.Completed, source.Id, ct);
        }

        Dictionary<string, string> labels = Labels((SnapshotLabel, Format(snapshot.Value)), (SourceLabel, Format(sandbox.Value)));
        Operation<DiskImage> committed = await client.CommitSandboxAsync(WaitUntil.Completed, source.Id, labels, ct);
        if (suspended)
        {
            await client.StopSandboxAsync(WaitUntil.Completed, source.Id, ct);
        }

        return new Result<SnapshotObservation>(ObserveSnapshot(committed.Value));
    }

    public async IAsyncEnumerable<SnapshotObservation> ListSnapshotsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (DiskImage image in client.GetDiskImagesAsync(Labels(), ct))
        {
            bool snapshot = image.State == DiskImageState.Ready && ParseKey(image.Labels, SnapshotLabel) is not null && ParseKey(image.Labels, SourceLabel) is not null;
            if (snapshot)
            {
                yield return ObserveSnapshot(image);
            }
        }
    }

    public async Task DeleteSnapshotAsync(SnapshotKey snapshot, CancellationToken ct)
    {
        // Sandboxes created from the disk image keep running without it.
        await foreach (DiskImage image in client.GetDiskImagesAsync(Labels((SnapshotLabel, Format(snapshot.Value))), ct))
        {
            await client.DeleteDiskImageAsync(image.Id, ct);
        }
    }

    private async Task<Result<SandboxObservation>> CreateFromImageAsync(SandboxSpec spec, SandboxImage image, string specLabel, CancellationToken ct)
    {
        Result<string> diskImage = await DiskImageOfAsync(image, ct);
        if (diskImage.Failed)
        {
            return new Result<SandboxObservation>(diskImage.Error);
        }

        return await CreateSandboxAsync(spec, AzureSource.FromDiskImage(diskImage.Output), specLabel, ct);
    }

    // The image's disk image, which Azure takes about ten seconds to make even from an image it has
    // seen, so one per image is shared by every deployment of the sandbox group: the same public image
    // is the same disk image for all. One that is a day old is made again; the older ones go then, and
    // sandboxes made from them keep working.
    private async Task<Result<string>> DiskImageOfAsync(SandboxImage image, CancellationToken ct)
    {
        string imageLabel = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(image.Reference)))[..SpecLabelLength];
        Dictionary<string, string> labels = new Dictionary<string, string>(StringComparer.Ordinal) { [ImageLabel] = imageLabel };
        List<DiskImage> made = await client.GetDiskImagesAsync(labels, ct).ToListAsync(ct);
        DiskImage? newest = made.Where(found => found.State == DiskImageState.Ready).MaxBy(found => found.CreatedAt);
        if (newest is { CreatedAt: DateTimeOffset createdAt } && time.GetUtcNow() - createdAt < ImageFreshFor)
        {
            return new Result<string>(newest.Id);
        }

        DiskImageCreateOptions options = new DiskImageCreateOptions(image.Reference);
        options.Labels[ImageLabel] = imageLabel;
        Operation<DiskImage> making;
        try
        {
            making = await client.CreateDiskImageAsync(WaitUntil.Completed, options, ct);
        }
        catch (RequestFailedException failed) when (failed.ErrorCode is "ImageNotFound" or "RegistryAuthFailed" or "InvalidRequest")
        {
            // Registries answer a missing repository as one that needs signing in.
            return new Result<string>(Error.Validation("image", "Azure can't pull the image " + image.Reference + " (" + failed.ErrorCode + "); it must exist and be public."));
        }

        foreach (DiskImage older in made)
        {
            await client.DeleteDiskImageAsync(older.Id, ct);
        }

        // Those of images no sandbox was made from for two days go too, such as an older version's: one
        // in use is made again daily.
        DateTimeOffset unusedSince = time.GetUtcNow() - (2 * ImageFreshFor);
        await foreach (DiskImage unused in client.GetDiskImagesAsync(labels: null, ct))
        {
            if (unused.Labels.ContainsKey(ImageLabel) && unused.CreatedAt < unusedSince)
            {
                await client.DeleteDiskImageAsync(unused.Id, ct);
            }
        }

        return new Result<string>(making.Value.Id);
    }

    private async Task<Result<SandboxObservation>> CreateFromSnapshotAsync(SandboxSpec spec, SnapshotKey snapshot, string specLabel, CancellationToken ct)
    {
        DiskImage? saved = await FindSnapshotAsync(snapshot, ct);
        if (saved is null)
        {
            return new Result<SandboxObservation>(Error.NotFound("sandboxing.snapshot_not_found", "The snapshot doesn't exist."));
        }

        return await CreateSandboxAsync(spec, AzureSource.FromDiskImage(saved.Id), specLabel, ct);
    }

    private async Task<Result<SandboxObservation>> CreateSandboxAsync(SandboxSpec spec, AzureSource source, string specLabel, CancellationToken ct)
    {
        string cpu = spec.Resources.CpuMillicores.ToString(CultureInfo.InvariantCulture) + "m";
        string memory = spec.Resources.MemoryMebibytes.ToString(CultureInfo.InvariantCulture) + "Mi";
        string disk = (spec.Resources.CpuMillicores * (long)DiskMebibytesPerCore / 1000).ToString(CultureInfo.InvariantCulture) + "Mi";
        SandboxCreateOptions options = new SandboxCreateOptions(source, new AzureResources(cpu, memory, disk))
        {
            AutoSuspend = SandboxAutoSuspend.AfterIdle(SafetySuspend, SandboxSuspendMode.Memory),
        };
        options.Labels[ScopeLabel] = scope;
        options.Labels[KeyLabel] = Format(spec.Key.Value);
        options.Labels[SpecLabel] = specLabel;
        foreach (KeyValuePair<string, string> variable in spec.Environment)
        {
            options.Environment[variable.Key] = variable.Value;
        }

        try
        {
            Operation<AzureSandbox> created = await client.CreateSandboxAsync(WaitUntil.Completed, options, ct);
            return new Result<SandboxObservation>(Observe(created.Value));
        }
        catch (RequestFailedException failed) when (failed.Status == 400)
        {
            return new Result<SandboxObservation>(Error.Validation("spec", "Azure can't run the sandbox (" + failed.ErrorCode + ")."));
        }
    }

    private static Result<SandboxObservation> Repeated(AzureSandbox sandbox, string specLabel)
    {
        return string.Equals(sandbox.Labels.GetValueOrDefault(SpecLabel), specLabel, StringComparison.Ordinal)
            ? new Result<SandboxObservation>(Observe(sandbox))
            : new Result<SandboxObservation>(Error.Conflict("sandboxing.key_in_use", "A sandbox with this key exists with a different spec."));
    }

    private static SandboxObservation Observe(AzureSandbox sandbox)
    {
        Guid? key = ParseKey(sandbox.Labels, KeyLabel);
        if (key is null)
        {
            throw new InvalidOperationException("Azure sandbox " + sandbox.Id + " has no sandbox key label.");
        }

        if (sandbox.CreatedAt is not DateTimeOffset createdAt)
        {
            throw new InvalidOperationException("Azure sandbox " + sandbox.Id + " has no creation time.");
        }

        SandboxState state = StateOf(sandbox.State);
        string? reason = state == SandboxState.Failed ? "Azure reports the sandbox " + sandbox.State + "." : null;
        return new SandboxObservation(SandboxKey.From(key.Value), state, createdAt, reason);
    }

    // A stopped sandbox keeps its memory; Idle is a running one nothing uses.
    private static SandboxState StateOf(AzureState state)
    {
        if (state == AzureState.Creating)
        {
            return SandboxState.Starting;
        }

        if (state == AzureState.Running || state == AzureState.Idle)
        {
            return SandboxState.Running;
        }

        if (state == AzureState.Stopping)
        {
            return SandboxState.Suspending;
        }

        if (state == AzureState.Stopped)
        {
            return SandboxState.Paused;
        }

        // StopFailed, or a state this provider doesn't know: the sandbox isn't usable.
        return SandboxState.Failed;
    }

    private static SnapshotObservation ObserveSnapshot(DiskImage image)
    {
        Guid? snapshot = ParseKey(image.Labels, SnapshotLabel);
        Guid? source = ParseKey(image.Labels, SourceLabel);
        if (snapshot is null || source is null)
        {
            throw new InvalidOperationException("Azure disk image " + image.Id + " has no snapshot or source label.");
        }

        if (image.CreatedAt is not DateTimeOffset createdAt)
        {
            throw new InvalidOperationException("Azure disk image " + image.Id + " has no creation time.");
        }

        return new SnapshotObservation(SnapshotKey.From(snapshot.Value), SandboxKey.From(source.Value), createdAt);
    }

    private static Guid? ParseKey(IReadOnlyDictionary<string, string> labels, string name)
    {
        bool parsed = Guid.TryParseExact(labels.GetValueOrDefault(name), "N", out Guid key);
        return parsed ? key : null;
    }

    private static string Format(Guid value)
    {
        return value.ToString("N", CultureInfo.InvariantCulture);
    }

    private async Task<AzureSandbox?> FindAsync(SandboxKey key, CancellationToken ct)
    {
        await foreach (AzureSandbox sandbox in client.GetSandboxesAsync(Labels((KeyLabel, Format(key.Value))), ct))
        {
            return sandbox;
        }

        return null;
    }

    private async Task<DiskImage?> FindSnapshotAsync(SnapshotKey snapshot, CancellationToken ct)
    {
        await foreach (DiskImage image in client.GetDiskImagesAsync(Labels((SnapshotLabel, Format(snapshot.Value))), ct))
        {
            return image;
        }

        return null;
    }

    // This scope's label, and others to select by.
    private Dictionary<string, string> Labels(params (string Name, string Value)[] others)
    {
        Dictionary<string, string> labels = new Dictionary<string, string>(StringComparer.Ordinal) { [ScopeLabel] = scope };
        foreach ((string name, string value) in others)
        {
            labels[name] = value;
        }

        return labels;
    }
}
