using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// Identifies an agent account.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<AgentAccountId>))]
public readonly record struct AgentAccountId : ITypedId<AgentAccountId>
{
    private AgentAccountId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static AgentAccountId New()
    {
        return new AgentAccountId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static AgentAccountId From(Guid value)
    {
        return new AgentAccountId(value);
    }
}
