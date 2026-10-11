namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>A kind of account as this host offers it.</summary>
/// <param name="Kind">The kind.</param>
/// <param name="Plan">
/// Whether it is a plan: a person's subscription, added by signing in at its vendor and only ever
/// their own. Otherwise it is an API key, added with its secret, a workspace's or someone's own.
/// </param>
/// <param name="Allowed">Whether this host allows adding it.</param>
public sealed record AgentAccountKindSummary(AgentAccountKind Kind, bool Plan, bool Allowed);
