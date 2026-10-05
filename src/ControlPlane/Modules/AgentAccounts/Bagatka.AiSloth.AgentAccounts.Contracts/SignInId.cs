using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// Identifies a sign-in in progress (<see cref="IAgentAccountsApi.StartSignInAsync"/>).
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<SignInId>))]
public readonly record struct SignInId : ITypedId<SignInId>
{
    private SignInId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static SignInId New()
    {
        return new SignInId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static SignInId From(Guid value)
    {
        return new SignInId(value);
    }
}
