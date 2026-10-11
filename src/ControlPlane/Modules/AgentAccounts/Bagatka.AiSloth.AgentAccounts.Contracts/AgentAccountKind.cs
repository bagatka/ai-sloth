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

    /// <summary>A GitHub Copilot plan, added by signing in at GitHub with a code; its token reaches only the owner's Copilot. Personal only.</summary>
    CopilotPlan = 2,

    /// <summary>
    /// A Claude plan (Pro or Max), added by signing in the way Claude Code does, approving at Claude and
    /// bringing back the code its page shows (<see cref="IAgentAccountsApi.StartSignInAsync"/>): Claude
    /// Code's calls go to Anthropic's API on the plan, which shares its five-hour and weekly limits.
    /// Runs Claude Code only. Personal only, where the deployment allows Claude plans. It never enters
    /// a nook.
    /// </summary>
    ClaudePlan = 3,

    /// <summary>
    /// An OpenAI API key, billed per token to its owner, or a key for another endpoint that speaks
    /// OpenAI's Responses API. It never enters a nook.
    /// </summary>
    OpenAIApiKey = 4,

    /// <summary>
    /// A ChatGPT plan (Plus or Pro), added by signing in the way Codex does, with a code entered at
    /// OpenAI (<see cref="IAgentAccountsApi.StartSignInAsync"/>): Codex's calls go to ChatGPT's Codex
    /// backend, which shares the plan's five-hour and weekly limits. Runs Codex only. Personal only,
    /// where the deployment allows ChatGPT plans. It never enters a nook.
    /// </summary>
    ChatGptPlan = 5,

    /// <summary>
    /// An OpenRouter API key, billed per token to its owner: one key for many vendors' models, through
    /// Anthropic's Messages API or OpenAI's APIs at OpenRouter. It never enters a nook.
    /// </summary>
    OpenRouterApiKey = 6,
}
