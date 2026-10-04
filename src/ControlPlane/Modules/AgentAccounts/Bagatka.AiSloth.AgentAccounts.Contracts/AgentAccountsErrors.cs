using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// Errors callers of <see cref="IAgentAccountsApi"/> may branch on.
/// </summary>
public static class AgentAccountsErrors
{
    /// <summary>The account doesn't exist, or the actor may not use it.</summary>
    public static readonly Error NotFound = Error.NotFound("agent_accounts.not_found", "Agent account not found.");
}
