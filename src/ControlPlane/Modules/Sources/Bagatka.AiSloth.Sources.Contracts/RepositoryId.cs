using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>
/// Identifies a repository a workspace connected.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<RepositoryId>))]
public readonly record struct RepositoryId : ITypedId<RepositoryId>
{
    private RepositoryId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static RepositoryId New()
    {
        return new RepositoryId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static RepositoryId From(Guid value)
    {
        return new RepositoryId(value);
    }
}
