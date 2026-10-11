using Bagatka.Harnesses;
using Bagatka.ModelAccess;

namespace Bagatka.AiSloth.Chats.Harness;

// What each kind of agent account is to a harness. Agent accounts and harnesses know nothing of each
// other; this is where they meet.
internal static class AccountCredentials
{
    public static CredentialKind KindOf(ModelAccessKind kind)
    {
        return kind switch
        {
            ModelAccessKind.AnthropicApiKey => CredentialKind.AnthropicApi,
            ModelAccessKind.OpenRouterApiKey => CredentialKind.OpenRouterApi,
            ModelAccessKind.OpenAIApiKey => CredentialKind.OpenAIApi,
            ModelAccessKind.CopilotPlan => CredentialKind.CopilotApi,
            ModelAccessKind.ChatGptPlan => CredentialKind.CodexApi,
            ModelAccessKind.ClaudePlan => CredentialKind.ClaudePlanApi,
        };
    }
}
