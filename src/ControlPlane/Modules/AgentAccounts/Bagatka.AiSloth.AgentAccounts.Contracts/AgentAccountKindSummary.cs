namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>A kind of account as this host offers it.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="AddedBySignIn">Whether it is added by signing in at its vendor rather than with its secret.</param>
/// <param name="PersonalOnly">Whether it can only be someone's own, as plans are.</param>
/// <param name="Allowed">Whether this host allows adding it.</param>
public sealed record AgentAccountKindSummary(AgentAccountKind Kind, bool AddedBySignIn, bool PersonalOnly, bool Allowed);
