using System;
using System.Text.Json.Serialization;

namespace Bagatka.Foundation;

/// <summary>
/// Identifies a user. It lives in Foundation, not in a module, because <see cref="Actor"/> needs it.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<UserId>))]
public readonly record struct UserId : ITypedId<UserId>
{
    private UserId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static UserId New()
    {
        return new UserId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static UserId From(Guid value)
    {
        return new UserId(value);
    }
}
