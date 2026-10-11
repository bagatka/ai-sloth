using System;
using Bagatka.Sdk.Anthropic;
using Bagatka.Sdk.OpenRouter;

namespace Bagatka.ModelAccess;

/// <summary>Where the vendors are, and the GitHub app Copilot plans sign in to. Tests point the vendors at fakes.</summary>
public sealed record ModelAccessSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="copilotClientId">
    /// The GitHub OAuth app people sign in to with a code to add their Copilot plans, by its client ID,
    /// which is public; <see langword="null"/> for Bagatka's own (named AiSloth at GitHub). It asks for
    /// no scopes, so its tokens reach nothing but the person's Copilot, and never expires its tokens,
    /// since they are never renewed.
    /// </param>
    /// <param name="anthropicApi">Anthropic's API; <see langword="null"/> for <c>https://api.anthropic.com</c>.</param>
    /// <param name="openRouterApi">OpenRouter's API; <see langword="null"/> for <c>https://openrouter.ai/api</c>.</param>
    /// <param name="copilotApi">Copilot's API; <see langword="null"/> for <c>https://api.githubcopilot.com</c>.</param>
    /// <param name="chatGptAuthority">OpenAI's authorization server, where Codex signs in; <see langword="null"/> for <c>https://auth.openai.com</c>.</param>
    /// <param name="chatGptBackend">ChatGPT's backend, its Codex backend at <c>/codex</c>; <see langword="null"/> for <c>https://chatgpt.com/backend-api</c>.</param>
    /// <param name="claudeAuthorizeUrl">Claude's page where a person approves Claude Code's sign-in; <see langword="null"/> for Anthropic's.</param>
    /// <param name="claudeTokenUrl">Where Claude Code's sign-in gets and renews its tokens; <see langword="null"/> for Anthropic's.</param>
    /// <param name="modelsDevUrl">models.dev's catalog, read daily; <see langword="null"/> for <c>https://models.dev/api.json</c>.</param>
    public ModelAccessSettings(
        string? copilotClientId = null,
        Uri? anthropicApi = null,
        Uri? openRouterApi = null,
        Uri? copilotApi = null,
        Uri? chatGptAuthority = null,
        Uri? chatGptBackend = null,
        Uri? claudeAuthorizeUrl = null,
        Uri? claudeTokenUrl = null,
        Uri? modelsDevUrl = null)
    {
        CopilotClientId = string.IsNullOrWhiteSpace(copilotClientId) ? BagatkaCopilotApp : copilotClientId;
        AnthropicApi = anthropicApi ?? new Uri("https://api.anthropic.com");
        OpenRouterApi = openRouterApi ?? OpenRouterSettings.Public.ApiUrl;
        CopilotApi = copilotApi ?? Bagatka.Sdk.GitHub.CopilotApi.Public;
        ChatGptAuthority = chatGptAuthority ?? new Uri("https://auth.openai.com");
        ChatGptBackend = chatGptBackend ?? new Uri("https://chatgpt.com/backend-api");
        ClaudeAuthorizeUrl = claudeAuthorizeUrl ?? ClaudeSignInSettings.Public.AuthorizeUrl;
        ClaudeTokenUrl = claudeTokenUrl ?? ClaudeSignInSettings.Public.TokenUrl;
        ModelsDevUrl = modelsDevUrl ?? new Uri("https://models.dev/api.json");
    }

    /// <summary>The GitHub OAuth app Copilot plans sign in to.</summary>
    public string CopilotClientId { get; }

    /// <summary>Anthropic's API: Anthropic API keys without an endpoint of their own, and Claude plans.</summary>
    public Uri AnthropicApi { get; }

    /// <summary>OpenRouter's API: its keys' calls and its catalog of models.</summary>
    public Uri OpenRouterApi { get; }

    /// <summary>Copilot's API: a Copilot plan's calls and its models.</summary>
    public Uri CopilotApi { get; }

    /// <summary>OpenAI's authorization server, where Codex signs in.</summary>
    public Uri ChatGptAuthority { get; }

    /// <summary>ChatGPT's backend, where a ChatGPT plan's usage is asked.</summary>
    public Uri ChatGptBackend { get; }

    /// <summary>ChatGPT's Codex backend, where a ChatGPT plan's calls go and its models are listed.</summary>
    public Uri CodexApi => new Uri(ChatGptBackend.AbsoluteUri.TrimEnd('/') + "/codex");

    /// <summary>Claude's page where a person approves Claude Code's sign-in.</summary>
    public Uri ClaudeAuthorizeUrl { get; }

    /// <summary>Where Claude Code's sign-in gets and renews its tokens.</summary>
    public Uri ClaudeTokenUrl { get; }

    /// <summary>models.dev's catalog of models.</summary>
    public Uri ModelsDevUrl { get; }

    // Bagatka's GitHub OAuth app for Copilot plans: no scopes, tokens that never expire, device flow on.
    private const string BagatkaCopilotApp = "Ov23li6XFDuFcx4wXfCZ";
}
