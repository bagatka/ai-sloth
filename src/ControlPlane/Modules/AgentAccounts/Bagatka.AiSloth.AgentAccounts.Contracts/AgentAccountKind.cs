namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// What an agent account is at its vendor, and so which harnesses can use it.
/// </summary>
public enum AgentAccountKind
{
    /// <summary>An Anthropic API key, billed per token to its owner. It never enters a nook.</summary>
    AnthropicApiKey = 1,

    /// <summary>A GitHub fine-grained personal access token with the Copilot Requests permission, drawing on its owner's Copilot plan. Personal only.</summary>
    GitHubCopilotToken = 2,

    /// <summary>
    /// A Claude subscription's token, from <c>claude setup-token</c>, drawing on its owner's Claude
    /// plan. Personal only, and only where the deployment allows it: Anthropic's terms forbid storing
    /// these without its written permission.
    /// </summary>
    ClaudeSubscription = 3,
}
