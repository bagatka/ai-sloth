namespace Bagatka.Harnesses;

/// <summary>
/// What a harness can pay for its model with.
/// </summary>
public enum CredentialKind
{
    /// <summary>An Anthropic API key, billed per token.</summary>
    AnthropicApiKey = 1,

    /// <summary>A GitHub fine-grained personal access token with the Copilot Requests permission, drawing on a Copilot plan.</summary>
    GitHubToken = 2,

    /// <summary>A Claude subscription's OAuth token, as <c>claude setup-token</c> prints it, drawing on a Claude plan.</summary>
    ClaudeOAuthToken = 3,
}
