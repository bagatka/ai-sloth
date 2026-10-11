using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagatka.AiSloth.Cli;

// The kinds of agent account as people name them: a plan after the product they subscribe to, an API
// key after the API it calls, so ChatGPT and OpenAI, or Claude and Anthropic, are never confused. The
// host says which kinds it knows and allows; this says how sloth shows and asks for each.
internal static class AccountKinds
{
    // SecretPrompt names what adding an API key asks for, SignsInWith where people sign in to add a plan,
    // and Endpoint another endpoint that speaks its API, for the kinds that take one.
    internal sealed record Kind(string Id, string HostKind, string Name, string Description, string? SecretPrompt, string? SignsInWith, string? Endpoint);

    public static IReadOnlyList<Kind> All { get; } =
    [
        new Kind("chatgpt-plan", "ChatGptPlan", "ChatGPT plan", "ChatGPT Plus or Pro", SecretPrompt: null, SignsInWith: "OpenAI", Endpoint: null),
        new Kind("claude-plan", "ClaudePlan", "Claude plan", "Claude Pro or Max", SecretPrompt: null, SignsInWith: "Claude", Endpoint: null),
        new Kind("copilot-plan", "CopilotPlan", "Copilot plan", "GitHub Copilot", SecretPrompt: null, SignsInWith: "GitHub", Endpoint: null),
        new Kind("openai-api-key", "OpenAIApiKey", "OpenAI API key", "OpenAI's API, or any endpoint that speaks it", "API key", SignsInWith: null, "https://gateway.example.com/v1"),
        new Kind("anthropic-api-key", "AnthropicApiKey", "Anthropic API key", "Anthropic's API, or any endpoint that speaks it", "API key", SignsInWith: null, "https://gateway.example.com"),
        new Kind("openrouter-api-key", "OpenRouterApiKey", "OpenRouter API key", "Many vendors' models with one key", "API key", SignsInWith: null, Endpoint: null),
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
