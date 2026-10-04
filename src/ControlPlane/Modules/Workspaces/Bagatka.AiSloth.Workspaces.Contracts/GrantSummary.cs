using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>Someone given access to a resource directly, not through what it is in.</summary>
/// <param name="UserId">Who.</param>
/// <param name="Access">How much they may do.</param>
public sealed record GrantSummary(UserId UserId, AccessLevel Access);
