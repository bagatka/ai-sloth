using System;
using System.Collections.Generic;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// Creates the models the service returns, for tests that stand in for <see cref="SandboxGroupClient"/>.
/// </summary>
public static class SandboxesModelFactory
{
    /// <summary>A sandbox as the service would return it.</summary>
    public static Sandbox Sandbox(
        string id,
        SandboxState state,
        IReadOnlyDictionary<string, string>? labels = null,
        SandboxResources? resources = null,
        DateTimeOffset? createdAt = null,
        string? diskImageId = null,
        string? snapshotId = null,
        string? region = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new Sandbox(id, state, labels ?? new Dictionary<string, string>(StringComparer.Ordinal), resources, createdAt, diskImageId, snapshotId, region);
    }

    /// <summary>A disk image as the service would return it.</summary>
    public static DiskImage DiskImage(
        string id,
        DiskImageState state,
        string? name = null,
        IReadOnlyDictionary<string, string>? labels = null,
        string? baseImage = null,
        string? errorMessage = null,
        DateTimeOffset? createdAt = null,
        long? sizeInMegabytes = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return new DiskImage(id, name, labels ?? new Dictionary<string, string>(StringComparer.Ordinal), baseImage, state, errorMessage, createdAt, sizeInMegabytes);
    }
}
