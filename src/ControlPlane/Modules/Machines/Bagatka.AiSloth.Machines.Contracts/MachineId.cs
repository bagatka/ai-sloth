using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// Identifies a machine.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<MachineId>))]
public readonly record struct MachineId : ITypedId<MachineId>
{
    private MachineId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static MachineId New()
    {
        return new MachineId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static MachineId From(Guid value)
    {
        return new MachineId(value);
    }
}
