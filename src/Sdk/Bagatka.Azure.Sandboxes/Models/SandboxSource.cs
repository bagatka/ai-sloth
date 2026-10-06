using System;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// What a new sandbox starts from: a disk image, or a snapshot, which brings back a sandbox's memory
/// and disk.
/// </summary>
public sealed class SandboxSource
{
    private SandboxSource(string? diskImageId, string? snapshotId)
    {
        DiskImageId = diskImageId;
        SnapshotId = snapshotId;
    }

    /// <summary>The disk image it starts from, or <see langword="null"/>.</summary>
    public string? DiskImageId { get; }

    /// <summary>The snapshot it starts from, or <see langword="null"/>.</summary>
    public string? SnapshotId { get; }

    /// <summary>A sandbox started from one of the group's disk images, with its own labels and environment.</summary>
    /// <param name="diskImageId">The disk image's ID.</param>
    public static SandboxSource FromDiskImage(string diskImageId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(diskImageId);
        return new SandboxSource(diskImageId, snapshotId: null);
    }

    /// <summary>
    /// A sandbox restored from a snapshot, memory included. The service takes no labels or
    /// environment for it: the restored processes keep the ones they had.
    /// </summary>
    /// <param name="snapshotId">The snapshot's ID.</param>
    public static SandboxSource FromSnapshot(string snapshotId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);
        return new SandboxSource(diskImageId: null, snapshotId);
    }
}
