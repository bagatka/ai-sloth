using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// Errors callers of <see cref="IAgentAccountsApi"/> may branch on.
/// </summary>
public static class AgentAccountsErrors
{
    /// <summary>The account doesn't exist, or the actor may not use it.</summary>
    public static readonly Error NotFound = Error.NotFound("agent_accounts.not_found", "Agent account not found.");

    /// <summary>The sign-in doesn't exist, isn't the actor's, or expired: start a new one.</summary>
    public static readonly Error SignInNotFound = Error.NotFound("agent_accounts.sign_in_not_found", "Sign-in not found or expired; start again.");

    /// <summary>The account's sign-in ended, such as a ChatGPT plan whose owner disconnected the app: remove it and add it again.</summary>
    public static readonly Error SignInEnded = Error.Conflict("agent_accounts.sign_in_ended", "The account's sign-in ended; remove it and sign in again.");
}
