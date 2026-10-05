using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagatka.AiSloth.Cli;

// The kinds of agent account as people name them: a plan after the product they subscribe to, an API
// key after the API it calls, so ChatGPT and OpenAI, or Claude and Anthropic, are never confused. The
// host says which kinds it knows and allows; this says how sloth shows and asks for each.
internal static class AccountKinds
{
    // SecretPrompt names what adding it asks for; SignsInWith, where people sign in to add it instead.
    internal sealed record Kind(string Id, string HostKind, bool IsPlan, string Name, string Description, string? SecretPrompt, string? SignsInWith);

    public static IReadOnlyList<Kind> All { get; } =
    [
        new Kind("chatgpt-plan", "ChatGptPlan", IsPlan: true, "ChatGPT plan", "ChatGPT Plus or Pro", SecretPrompt: null, SignsInWith: "ChatGPT"),
        new Kind("claude-plan", "ClaudePlan", IsPlan: true, "Claude plan", "Claude Pro or Max", "Token from claude setup-token", SignsInWith: null),
        new Kind("copilot-plan", "CopilotPlan", IsPlan: true, "Copilot plan", "GitHub Copilot", "GitHub token with the Copilot Requests permission", SignsInWith: null),
        new Kind("openai-api-key", "OpenAIApiKey", IsPlan: false, "OpenAI API key", "OpenAI's API, or any endpoint that speaks it", "API key", SignsInWith: null),
        new Kind("anthropic-api-key", "AnthropicApiKey", IsPlan: false, "Anthropic API key", "Anthropic's API, or any endpoint that speaks it", "API key", SignsInWith: null),
    ];

    public static Kind? Find(string id)
    {
        return All.SingleOrDefault(kind => string.Equals(kind.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    // A kind as the host names it; null for one this sloth doesn't know yet.
    public static Kind? FromHost(string hostKind)
    {
        return All.SingleOrDefault(kind => string.Equals(kind.HostKind, hostKind, StringComparison.Ordinal));
    }
}
