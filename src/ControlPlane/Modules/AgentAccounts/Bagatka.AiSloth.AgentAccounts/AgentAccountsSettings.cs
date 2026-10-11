using System;
using Bagatka.Sdk.OpenAI;
using Bagatka.Sdk.OpenRouter;

namespace Bagatka.AiSloth.AgentAccounts;

/// <summary>
/// What the AgentAccounts module needs from its host.
/// </summary>
public sealed record AgentAccountsSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="allowClaudePlans">
    /// Whether people may add Claude plans. Off unless Anthropic has given this deployment
    /// written permission: its terms forbid storing Claude sign-in tokens otherwise.
    /// </param>
    /// <param name="allowChatGptPlans">
    /// Whether people may sign in with ChatGPT to add their plans. OpenAI lets open-source and
    /// self-hosted deployments do so; a hosted service for other people needs OpenAI's approval first.
    /// </param>
    /// <param name="chatGptAuthority">OpenAI's authorization server; <see langword="null"/> for <c>https://auth.openai.com</c>. Tests change it.</param>
    /// <param name="chatGptApi">The API a ChatGPT plan's calls go to; <see langword="null"/> for <c>https://api.openai.com/v1</c>. Tests change it.</param>
    /// <param name="openRouterApi">OpenRouter's API, its models' calls and catalog; <see langword="null"/> for <c>https://openrouter.ai/api</c>. Tests change it.</param>
    /// <param name="copilotApi">Copilot's API, where a Copilot plan's calls go and its models are listed; <see langword="null"/> for <c>https://api.githubcopilot.com</c>. Tests change it.</param>
    /// <param name="copilotClientId">
    /// The GitHub OAuth app people sign in to with a code to add their Copilot plans, by its client ID,
    /// which is public; <see langword="null"/> for AiSloth's own. It asks for no scopes, so its tokens
    /// reach nothing but the person's Copilot, and they never expire, so this host never renews them.
    /// </param>
    public AgentAccountsSettings(
        bool allowClaudePlans = false,
        bool allowChatGptPlans = false,
        Uri? chatGptAuthority = null,
        Uri? chatGptApi = null,
        Uri? openRouterApi = null,
        Uri? copilotApi = null,
        string? copilotClientId = null)
    {
        CopilotClientId = string.IsNullOrWhiteSpace(copilotClientId) ? AiSlothCopilotApp : copilotClientId;
        CopilotApi = copilotApi ?? Bagatka.Sdk.GitHub.CopilotApi.Public;
        AllowClaudePlans = allowClaudePlans;
        AllowChatGptPlans = allowChatGptPlans;
        ChatGptAuthority = chatGptAuthority ?? new Uri("https://auth.openai.com");
        ChatGptApi = chatGptApi ?? ChatGptSignInClient.Resource;
        ChatGptSignIn = new ChatGptSignInSettings(ChatGptAuthority);
        OpenRouterApi = openRouterApi ?? OpenRouterSettings.Public.ApiUrl;
    }

    /// <summary>Whether people may add Claude plans.</summary>
    public bool AllowClaudePlans { get; }

    /// <summary>Whether people may sign in with ChatGPT to add their plans.</summary>
    public bool AllowChatGptPlans { get; }

    /// <summary>OpenAI's authorization server.</summary>
    public Uri ChatGptAuthority { get; }

    /// <summary>The API a ChatGPT plan's calls go to.</summary>
    public Uri ChatGptApi { get; }

    /// <summary>The Sign in with ChatGPT client's settings.</summary>
    public ChatGptSignInSettings ChatGptSignIn { get; }

    /// <summary>OpenRouter's API: where an OpenRouter key's calls go, and its catalog of models.</summary>
    public Uri OpenRouterApi { get; }

    /// <summary>Copilot's API, where a Copilot plan's calls go and its models are listed.</summary>
    public Uri CopilotApi { get; }

    /// <summary>The GitHub OAuth app people sign in to for their Copilot plans, by its client ID.</summary>
    public string CopilotClientId { get; }

    // AiSloth's GitHub OAuth app for Copilot plans: no scopes, tokens that never expire, device flow on.
    private const string AiSlothCopilotApp = "Ov23li6XFDuFcx4wXfCZ";
}
