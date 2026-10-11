using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Identifies a preset.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<PresetId>))]
public readonly record struct PresetId : ITypedId<PresetId>
{
    private PresetId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static PresetId New()
    {
        return new PresetId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static PresetId From(Guid value)
    {
        return new PresetId(value);
    }
}
