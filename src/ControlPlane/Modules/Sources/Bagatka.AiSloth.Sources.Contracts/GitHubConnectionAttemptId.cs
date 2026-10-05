using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>
/// Identifies a person's attempt to connect their GitHub account.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<GitHubConnectionAttemptId>))]
public readonly record struct GitHubConnectionAttemptId : ITypedId<GitHubConnectionAttemptId>
{
    private GitHubConnectionAttemptId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static GitHubConnectionAttemptId New()
    {
        return new GitHubConnectionAttemptId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static GitHubConnectionAttemptId From(Guid value)
    {
        return new GitHubConnectionAttemptId(value);
    }
}
