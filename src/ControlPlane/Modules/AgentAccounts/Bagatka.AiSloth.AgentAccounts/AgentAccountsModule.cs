using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.OpenAI;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.AgentAccounts;

/// <summary>
/// Registers the AgentAccounts module: <see cref="IAgentAccountsApi"/> and its database.
/// </summary>
public static class AgentAccountsModule
{
    /// <summary>Registers the module. The host also registers a <see cref="System.TimeProvider"/> and the Workspaces module.</summary>
    public static IServiceCollection AddAgentAccountsModule(this IServiceCollection services, AgentAccountsSettings settings, EncryptionSettings encryption)
    {
        services.AddSingleton(settings);
        services.AddKeyedSingleton(AgentAccountsDbContext.Schema, new SecretBox(encryption, AgentAccountsDbContext.Schema));
        services.AddChatGptSignInClient(settings.ChatGptSignIn);
        services.AddModuleDbContext<AgentAccountsDbContext>(AgentAccountsDbContext.Schema);
        services.AddSingleton<IAgentAccountsApi, AgentAccountsApi>();
        return services;
    }
}
