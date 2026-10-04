using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Identifies a nook.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<NookId>))]
public readonly record struct NookId : ITypedId<NookId>
{
    private NookId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static NookId New()
    {
        return new NookId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static NookId From(Guid value)
    {
        return new NookId(value);
    }
}
