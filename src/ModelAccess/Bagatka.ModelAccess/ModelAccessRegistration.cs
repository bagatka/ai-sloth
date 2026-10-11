using System;
using Bagatka.ModelAccess.Catalog;
using Bagatka.ModelAccess.Kinds;
using Bagatka.Sdk.Anthropic;
using Bagatka.Sdk.GitHub;
using Bagatka.Sdk.OpenAI;
using Bagatka.Sdk.OpenRouter;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.ModelAccess;

/// <summary>Registers <see cref="ModelVendors"/>.</summary>
public static class ModelAccessRegistration
{
    /// <summary>
    /// Registers one <see cref="ModelVendors"/> for the whole application, with the vendors' clients it
    /// uses. The host registers <see cref="GitHubClient"/>, which Copilot plans sign in with, and a
    /// <see cref="TimeProvider"/>. Call it once.
    /// </summary>
    public static IServiceCollection AddModelAccess(this IServiceCollection services, ModelAccessSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        services.AddSingleton(settings);
        services.AddCodexSignInClient(settings.ChatGptAuthority);
        services.AddClaudeSignInClient(ClaudeSignInSettings.Public with { AuthorizeUrl = settings.ClaudeAuthorizeUrl, TokenUrl = settings.ClaudeTokenUrl });
        services.AddChatGptUsageClient();
        services.AddOpenAIModelsClient();
        services.AddAnthropicModelsClient();
        services.AddClaudePlanUsageClient();
        services.AddCopilotModelsClient();
        services.AddOpenRouterClient(new OpenRouterSettings(settings.OpenRouterApi));
        services.AddSingleton<ModelCatalog>();

        // Every kind, in the order to offer them: API keys, then plans.
        services.AddSingleton<IAccountKind, AnthropicApiKey>();
        services.AddSingleton<IAccountKind, OpenAIApiKey>();
        services.AddSingleton<IAccountKind, OpenRouterApiKey>();
        services.AddSingleton<IAccountKind, ClaudePlan>();
        services.AddSingleton<IAccountKind, ChatGptPlan>();
        services.AddSingleton<IAccountKind, CopilotPlan>();
        services.AddSingleton(provider => new ModelVendors(provider.GetServices<IAccountKind>(), provider.GetRequiredService<ModelCatalog>()));
        return services;
    }
}
