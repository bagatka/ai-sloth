using System;
using System.Collections.Generic;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// A sandbox: a microVM with its own kernel in a sandbox group.
/// </summary>
public sealed class Sandbox
{
    internal Sandbox(string id, SandboxState state, IReadOnlyDictionary<string, string> labels, SandboxResources? resources, DateTimeOffset? createdAt, string? diskImageId, string? snapshotId, string? region)
    {
        Id = id;
        State = state;
        Labels = labels;
        Resources = resources;
        CreatedAt = createdAt;
        DiskImageId = diskImageId;
        SnapshotId = snapshotId;
        Region = region;
    }

    /// <summary>The sandbox's ID, a GUID the service chose.</summary>
    public string Id { get; }

    /// <summary>Where it is in its lifecycle.</summary>
    public SandboxState State { get; }

    /// <summary>Its labels.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; }

    /// <summary>Its CPU, memory, and disk.</summary>
    public SandboxResources? Resources { get; }

    /// <summary>When it was created.</summary>
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>The disk image it started from, if any.</summary>
    public string? DiskImageId { get; }

    /// <summary>The snapshot its latest stop took, if any.</summary>
    public string? SnapshotId { get; }

    /// <summary>The Azure region it runs in, such as <c>eastus2</c>.</summary>
    public string? Region { get; }
}
