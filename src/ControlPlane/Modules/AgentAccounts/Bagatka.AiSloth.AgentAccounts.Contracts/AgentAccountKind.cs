namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// What an agent account is at its vendor, and so which harnesses can use it.
/// </summary>
public enum AgentAccountKind
{
    /// <summary>
    /// An Anthropic API key, billed per token to its owner, or a key for another endpoint that speaks
    /// Anthropic's Messages API. It never enters a nook.
    /// </summary>
    AnthropicApiKey = 1,

    /// <summary>A GitHub fine-grained personal access token with the Copilot Requests permission, drawing on its owner's Copilot plan. Personal only.</summary>
    CopilotPlan = 2,

    /// <summary>
    /// A Claude subscription's token, from <c>claude setup-token</c>, drawing on its owner's Claude
    /// plan. Personal only, and only where the deployment allows it: Anthropic's terms forbid storing
    /// these without its written permission.
    /// </summary>
    ClaudePlan = 3,

    /// <summary>
    /// An OpenAI API key, billed per token to its owner, or a key for another endpoint that speaks
    /// OpenAI's Responses API, such as OpenRouter's. It never enters a nook.
    /// </summary>
    OpenAIApiKey = 4,

    /// <summary>
    /// A ChatGPT plan, added by signing in with ChatGPT (<see cref="IAgentAccountsApi.StartSignInAsync"/>),
    /// drawing on its owner's plan. Personal only, and only where the deployment allows it: OpenAI lets
    /// open-source and self-hosted apps use it, and a hosted service needs OpenAI's approval. It never
    /// enters a nook.
    /// </summary>
    ChatGptPlan = 5,
}
