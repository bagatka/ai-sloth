using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Identifies one watch of a process's output, so the daemon's upload for it reaches the right watcher.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<WatchId>))]
public readonly record struct WatchId : ITypedId<WatchId>
{
    private WatchId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static WatchId New()
    {
        return new WatchId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static WatchId From(Guid value)
    {
        return new WatchId(value);
    }
}
