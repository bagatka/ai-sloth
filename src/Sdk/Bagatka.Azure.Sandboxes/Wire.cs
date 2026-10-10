using System;
using System.Collections.Generic;

namespace Bagatka.Azure.Sandboxes;

/// <summary>
/// The service's JSON shapes, only as much as this client reads and writes (2026-09-01-preview).
/// </summary>
internal static class Wire
{
    internal sealed record Sandbox(
        string Id,
        string? State,
        Dictionary<string, string>? Labels,
        Resources? Resources,
        DateTimeOffset? CreatedAt,
        Source? SourcesRef,
        string? SnapshotId,
        string? Region);

    internal sealed record Resources(string Cpu, string Memory, string? Disk);

    internal sealed record Source(DiskImageRef? DiskImage, SnapshotRef? Snapshot);

    internal sealed record DiskImageRef(string? Id);

    internal sealed record SnapshotRef(string Id);

    internal sealed record CreateSandbox(
        Source SourcesRef,
        Resources Resources,
        Dictionary<string, string>? Labels,
        Dictionary<string, string>? Environment,
        List<string>? Entrypoint,
        List<string>? Cmd,
        Lifecycle? Lifecycle);

    internal sealed record Lifecycle(AutoSuspendPolicy AutoSuspendPolicy, AutoDeletePolicy AutoDeletePolicy);

    internal sealed record AutoSuspendPolicy(bool Enabled, int? Interval, string? Mode);

    internal sealed record AutoDeletePolicy(bool Enabled);

    internal sealed record SandboxPage(List<Sandbox> Value, string? NextLink);

    internal sealed record DiskImage(string Id, string? Name, Dictionary<string, string>? Labels, ImageInfo? Image, DiskImageStatus? Status, long? SizeInMB);

    internal sealed record ImageInfo(string? Base);

    internal sealed record DiskImageStatus(string? State, string? ErrorMessage, DateTimeOffset? CreatedAt);

    internal sealed record DiskImagePage(List<DiskImage> Value, string? NextLink);

    internal sealed record CreateDiskImage(DiskImageSource Source, string? Name, Dictionary<string, string>? Labels);

    internal sealed record DiskImageSource(string Kind, string ImageUrl, RegistryAuthentication? Authentication);

    internal sealed record RegistryAuthentication(RegistryCredentials RegistryCredentials);

    internal sealed record RegistryCredentials(string Username, string Token);

    internal sealed record Commit(Dictionary<string, string>? Labels);

    internal sealed record CommitResult(DiskImage DiskImage);
}
