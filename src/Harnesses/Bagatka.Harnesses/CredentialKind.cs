namespace Bagatka.Harnesses;

/// <summary>
/// What a harness can pay for its model with. Model APIs are reached through a model gateway, so the
/// key or plan behind them never reaches the harness; tokens of plans tied to one harness go to it
/// (<see cref="HarnessProfile.EnvironmentFor"/>).
/// </summary>
public enum CredentialKind
{
    /// <summary>Anthropic's Messages API, through a model gateway: an Anthropic API key, or another endpoint speaking it.</summary>
    AnthropicApi = 1,

    /// <summary>A GitHub token that reaches its owner's Copilot plan; it goes to the harness.</summary>
    GitHubToken = 2,

    /// <summary>A Claude subscription's OAuth token, as <c>claude setup-token</c> prints it, drawing on a Claude plan; it goes to the harness.</summary>
    ClaudeOAuthToken = 3,

    /// <summary>OpenAI's Responses API, through a model gateway: an OpenAI API key, a ChatGPT plan, or another endpoint speaking it.</summary>
    OpenAIApi = 4,

    /// <summary>
    /// OpenRouter, through a model gateway at its API's root: Anthropic's Messages API at
    /// <c>/v1/messages</c> and OpenAI's chat API at <c>/v1</c>, each with OpenRouter's names for models.
    /// </summary>
    OpenRouterApi = 5,
}
