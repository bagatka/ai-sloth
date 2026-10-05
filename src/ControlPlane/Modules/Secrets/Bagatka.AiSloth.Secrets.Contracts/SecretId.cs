using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Secrets.Contracts;

/// <summary>
/// Identifies a secret.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<SecretId>))]
public readonly record struct SecretId : ITypedId<SecretId>
{
    private SecretId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static SecretId New()
    {
        return new SecretId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static SecretId From(Guid value)
    {
        return new SecretId(value);
    }
}
