using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// Identifies a session: one signed-in device.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<SessionId>))]
public readonly record struct SessionId : ITypedId<SessionId>
{
    private SessionId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static SessionId New()
    {
        return new SessionId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static SessionId From(Guid value)
    {
        return new SessionId(value);
    }
}
