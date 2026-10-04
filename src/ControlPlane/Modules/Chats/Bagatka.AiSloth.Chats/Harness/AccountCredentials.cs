using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.Harnesses;

namespace Bagatka.AiSloth.Chats.Harness;

// What each kind of agent account is to a harness. Agent accounts and harnesses know nothing of each
// other; this is where they meet.
internal static class AccountCredentials
{
    public static CredentialKind KindOf(AgentAccountKind kind)
    {
        return kind switch
        {
            AgentAccountKind.AnthropicApiKey => CredentialKind.AnthropicApiKey,
            AgentAccountKind.GitHubCopilotToken => CredentialKind.GitHubToken,
            AgentAccountKind.ClaudeSubscription => CredentialKind.ClaudeOAuthToken,
        };
    }
}
