namespace Bagatka.ModelAccess;

/// <summary>
/// A way to pay for a vendor's models: an API key, added with its secret, or a person's plan, signed in
/// as the vendor's own agent signs in. Stored by name.
/// </summary>
public enum ModelAccessKind
{
    /// <summary>An Anthropic API key, at Anthropic's API or another endpoint that speaks it.</summary>
    AnthropicApiKey = 1,

    /// <summary>A GitHub Copilot plan, signed in with a code entered at GitHub; Copilot's CLI and pi run on it.</summary>
    CopilotPlan = 2,

    /// <summary>
    /// A Claude plan (Pro or Max), signed in as Claude Code does, approving at Claude and bringing back
    /// the code its page shows; Claude Code runs on it, through Anthropic's API.
    /// </summary>
    ClaudePlan = 3,

    /// <summary>An OpenAI API key, at OpenAI's API or another endpoint that speaks it.</summary>
    OpenAIApiKey = 4,

    /// <summary>
    /// A ChatGPT plan (Plus or Pro), signed in as Codex does, with a code entered at OpenAI; Codex runs
    /// on it, through ChatGPT's Codex backend.
    /// </summary>
    ChatGptPlan = 5,

    /// <summary>An OpenRouter key: many vendors' models, through Anthropic's Messages API or OpenAI's APIs at OpenRouter.</summary>
    OpenRouterApiKey = 6,
}
