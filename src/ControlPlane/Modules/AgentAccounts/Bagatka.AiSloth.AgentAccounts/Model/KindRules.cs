using Bagatka.AiSloth.AgentAccounts.Contracts;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// What each kind of account is to this module, in one place for adding, signing in, and listing: how
// it is added, whether it is one person's only, and whether this deployment allows it. Every switch
// names every kind, so a new one fails the build until each rule decides.
internal static class KindRules
{
    public static bool Known(AgentAccountKind kind)
    {
        return kind is AgentAccountKind.AnthropicApiKey or AgentAccountKind.OpenAIApiKey or AgentAccountKind.OpenRouterApiKey
            or AgentAccountKind.ChatGptPlan or AgentAccountKind.ClaudePlan or AgentAccountKind.CopilotPlan or AgentAccountKind.CodexPlan;
    }

    public static bool AddedBySignIn(AgentAccountKind kind)
    {
        return kind switch
        {
            AgentAccountKind.ChatGptPlan or AgentAccountKind.CopilotPlan or AgentAccountKind.CodexPlan => true,
            AgentAccountKind.AnthropicApiKey or AgentAccountKind.OpenAIApiKey or AgentAccountKind.OpenRouterApiKey
                or AgentAccountKind.ClaudePlan => false,
        };
    }

    // Plans are one person's; a team shares a workspace's API key.
    public static bool PersonalOnly(AgentAccountKind kind)
    {
        return kind switch
        {
            AgentAccountKind.ChatGptPlan or AgentAccountKind.ClaudePlan or AgentAccountKind.CopilotPlan or AgentAccountKind.CodexPlan => true,
            AgentAccountKind.AnthropicApiKey or AgentAccountKind.OpenAIApiKey or AgentAccountKind.OpenRouterApiKey => false,
        };
    }

    // Plans a vendor allows hosted apps only with its permission are off until the deployment says so.
    public static bool Allowed(AgentAccountKind kind, AgentAccountsSettings settings)
    {
        return kind switch
        {
            AgentAccountKind.ClaudePlan => settings.AllowClaudePlans,
            AgentAccountKind.ChatGptPlan or AgentAccountKind.CodexPlan => settings.AllowChatGptPlans,
            AgentAccountKind.AnthropicApiKey or AgentAccountKind.OpenAIApiKey or AgentAccountKind.OpenRouterApiKey or AgentAccountKind.CopilotPlan => true,
        };
    }

    // Sign-ins with a code, entered at the vendor, rather than in a browser that returns to the caller.
    public static bool SignsInWithCode(AgentAccountKind kind)
    {
        return kind is AgentAccountKind.CopilotPlan or AgentAccountKind.CodexPlan;
    }
}
