using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Identifies a process in a nook. The control plane creates it before the daemon starts the process.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<ProcessId>))]
public readonly record struct ProcessId : ITypedId<ProcessId>
{
    private ProcessId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static ProcessId New()
    {
        return new ProcessId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static ProcessId From(Guid value)
    {
        return new ProcessId(value);
    }
}
