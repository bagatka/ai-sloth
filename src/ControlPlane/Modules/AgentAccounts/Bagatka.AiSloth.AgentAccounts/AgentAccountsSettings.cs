using System;
using Bagatka.Sdk.Anthropic;
using Bagatka.Sdk.OpenRouter;

namespace Bagatka.AiSloth.AgentAccounts;

/// <summary>
/// What the AgentAccounts module needs from its host.
/// </summary>
public sealed record AgentAccountsSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="allowClaudePlans">
    /// Whether people may add Claude plans, signed in as Claude Code. Anthropic lets apps built on its
    /// Agent SDK use a person's own plan; a service that offers it to other people should agree it with
    /// Anthropic first.
    /// </param>
    /// <param name="allowChatGptPlans">
    /// Whether people may add ChatGPT plans, signed in as Codex. A service that offers it to other
    /// people should agree it with OpenAI first.
    /// </param>
    /// <param name="chatGptAuthority">OpenAI's authorization server, where Codex signs in; <see langword="null"/> for <c>https://auth.openai.com</c>. Tests change it.</param>
    /// <param name="openRouterApi">OpenRouter's API, its models' calls and catalog; <see langword="null"/> for <c>https://openrouter.ai/api</c>. Tests change it.</param>
    /// <param name="copilotApi">Copilot's API, where a Copilot plan's calls go and its models are listed; <see langword="null"/> for <c>https://api.githubcopilot.com</c>. Tests change it.</param>
    /// <param name="copilotClientId">
    /// The GitHub OAuth app people sign in to with a code to add their Copilot plans, by its client ID,
    /// which is public; <see langword="null"/> for AiSloth's own. It asks for no scopes, so its tokens
    /// reach nothing but the person's Copilot, and they never expire, so this host never renews them.
    /// </param>
    /// <param name="modelsDevUrl">
    /// models.dev's catalog of models, read daily for what's known of each model; <see langword="null"/>
    /// for <c>https://models.dev/api.json</c>. Tests change it.
    /// </param>
    /// <param name="chatGptBackend">
    /// ChatGPT's own backend, where a ChatGPT plan's calls (at <c>/codex</c>) and usage go;
    /// <see langword="null"/> for <c>https://chatgpt.com/backend-api</c>. Tests change it.
    /// </param>
    /// <param name="anthropicApi">
    /// Anthropic's API, where an Anthropic API key's calls go unless it names another endpoint, and a
    /// Claude plan's, its models, and its usage; <see langword="null"/> for <c>https://api.anthropic.com</c>.
    /// Tests change it.
    /// </param>
    /// <param name="claudeAuthorizeUrl">Claude's page where a person approves Claude Code's sign-in; <see langword="null"/> for Anthropic's. Tests change it.</param>
    /// <param name="claudeTokenUrl">Where Claude Code's sign-in gets and renews its tokens; <see langword="null"/> for Anthropic's. Tests change it.</param>
    public AgentAccountsSettings(
        bool allowClaudePlans = false,
        bool allowChatGptPlans = false,
        Uri? chatGptAuthority = null,
        Uri? openRouterApi = null,
        Uri? copilotApi = null,
        string? copilotClientId = null,
        Uri? modelsDevUrl = null,
        Uri? anthropicApi = null,
        Uri? chatGptBackend = null,
        Uri? claudeAuthorizeUrl = null,
        Uri? claudeTokenUrl = null)
    {
        ClaudeAuthorizeUrl = claudeAuthorizeUrl ?? ClaudeSignInSettings.Public.AuthorizeUrl;
        ClaudeTokenUrl = claudeTokenUrl ?? ClaudeSignInSettings.Public.TokenUrl;
        ChatGptBackend = chatGptBackend ?? new Uri("https://chatgpt.com/backend-api");
        CodexApi = new Uri(ChatGptBackend.AbsoluteUri.TrimEnd('/') + "/codex");
        AnthropicApi = anthropicApi ?? new Uri("https://api.anthropic.com");
        ModelsDevUrl = modelsDevUrl ?? new Uri("https://models.dev/api.json");
        CopilotClientId = string.IsNullOrWhiteSpace(copilotClientId) ? AiSlothCopilotApp : copilotClientId;
        CopilotApi = copilotApi ?? Bagatka.Sdk.GitHub.CopilotApi.Public;
        AllowClaudePlans = allowClaudePlans;
        AllowChatGptPlans = allowChatGptPlans;
        ChatGptAuthority = chatGptAuthority ?? new Uri("https://auth.openai.com");
        OpenRouterApi = openRouterApi ?? OpenRouterSettings.Public.ApiUrl;
    }

    /// <summary>Whether people may add Claude plans.</summary>
    public bool AllowClaudePlans { get; }

    /// <summary>Whether people may add ChatGPT plans.</summary>
    public bool AllowChatGptPlans { get; }

    /// <summary>OpenAI's authorization server, where Codex signs in.</summary>
    public Uri ChatGptAuthority { get; }

    /// <summary>Claude's page where a person approves Claude Code's sign-in.</summary>
    public Uri ClaudeAuthorizeUrl { get; }

    /// <summary>Where Claude Code's sign-in gets and renews its tokens.</summary>
    public Uri ClaudeTokenUrl { get; }

    /// <summary>OpenRouter's API: where an OpenRouter key's calls go, and its catalog of models.</summary>
    public Uri OpenRouterApi { get; }

    /// <summary>Copilot's API, where a Copilot plan's calls go and its models are listed.</summary>
    public Uri CopilotApi { get; }

    /// <summary>The GitHub OAuth app people sign in to for their Copilot plans, by its client ID.</summary>
    public string CopilotClientId { get; }

    /// <summary>ChatGPT's own backend, where a ChatGPT plan's usage is asked.</summary>
    public Uri ChatGptBackend { get; }

    /// <summary>ChatGPT's Codex backend, where a ChatGPT plan's calls go and its models are listed.</summary>
    public Uri CodexApi { get; }

    /// <summary>Anthropic's API, for Anthropic API keys without an endpoint of their own, and Claude plans.</summary>
    public Uri AnthropicApi { get; }

    /// <summary>models.dev's catalog of models, read daily.</summary>
    public Uri ModelsDevUrl { get; }

    // AiSloth's GitHub OAuth app for Copilot plans: no scopes, tokens that never expire, device flow on.
    private const string AiSlothCopilotApp = "Ov23li6XFDuFcx4wXfCZ";
}
