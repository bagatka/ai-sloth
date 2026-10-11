using Bagatka.AiSloth.AgentAccounts.Catalog;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.AgentAccounts.Kinds;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.Anthropic;
using Bagatka.Sdk.GitHub;
using Bagatka.Sdk.OpenAI;
using Bagatka.Sdk.OpenRouter;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.AgentAccounts;

/// <summary>
/// Registers the AgentAccounts module: <see cref="IAgentAccountsApi"/> and its database.
/// </summary>
public static class AgentAccountsModule
{
    /// <summary>
    /// Registers the module. The host also registers a <see cref="System.TimeProvider"/>, the Workspaces
    /// module, and a <see cref="GitHubClient"/> (<c>AddGitHubClient</c>), which Copilot plans sign in through.
    /// </summary>
    public static IServiceCollection AddAgentAccountsModule(this IServiceCollection services, AgentAccountsSettings settings, EncryptionSettings encryption)
    {
        services.AddSingleton(settings);
        services.AddKeyedSingleton(AgentAccountsDbContext.Schema, new SecretBox(encryption, AgentAccountsDbContext.Schema));
        services.AddCodexSignInClient(settings.ChatGptAuthority);
        services.AddClaudeSignInClient(ClaudeSignInSettings.Public with { AuthorizeUrl = settings.ClaudeAuthorizeUrl, TokenUrl = settings.ClaudeTokenUrl });
        services.AddChatGptUsageClient();
        services.AddOpenAIModelsClient();
        services.AddAnthropicModelsClient();
        services.AddClaudePlanUsageClient();
        services.AddCopilotModelsClient();
        services.AddSingleton<ModelCatalog>();
        services.AddOpenRouterClient(new OpenRouterSettings(settings.OpenRouterApi));
        services.AddModuleDbContext<AgentAccountsDbContext>(AgentAccountsDbContext.Schema);

        // Every kind of account, in the order clients offer them: API keys, then plans.
        services.AddSingleton<IAccountKind, AnthropicApiKey>();
        services.AddSingleton<IAccountKind, OpenAIApiKey>();
        services.AddSingleton<IAccountKind, OpenRouterApiKey>();
        services.AddSingleton<IAccountKind, ClaudePlan>();
        services.AddSingleton<IAccountKind, ChatGptPlan>();
        services.AddSingleton<IAccountKind, CopilotPlan>();
        services.AddSingleton<AccountKinds>();
        services.AddSingleton<IAgentAccountsApi, AgentAccountsApi>();
        return services;
    }
}
