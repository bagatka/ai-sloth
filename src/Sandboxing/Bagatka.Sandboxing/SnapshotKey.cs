using System;

namespace Bagatka.Sandboxing;

/// <summary>
/// The caller's identifier for a snapshot. Providers derive resource names and tags from it, which
/// is what makes taking a snapshot safe to repeat.
/// </summary>
public readonly record struct SnapshotKey
{
    private SnapshotKey(Guid value)
    {
        Value = value;
    }

    /// <summary>The raw value.</summary>
    public Guid Value { get; }

    /// <summary>Wraps the caller's own snapshot identifier.</summary>
    public static SnapshotKey From(Guid value)
    {
        return new SnapshotKey(value);
    }
}
