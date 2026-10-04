using System;

namespace Bagatka.Sandboxing;

/// <summary>
/// A usable snapshot as its backend reports it.
/// </summary>
/// <param name="Key">The caller's identifier for the snapshot.</param>
/// <param name="Source">The sandbox it was taken from, which may since have been deleted.</param>
/// <param name="CreatedAt">When it became usable.</param>
public sealed record SnapshotObservation(SnapshotKey Key, SandboxKey Source, DateTimeOffset CreatedAt);
