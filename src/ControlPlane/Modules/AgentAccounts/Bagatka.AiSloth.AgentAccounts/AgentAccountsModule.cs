using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Foundation.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.AgentAccounts;

/// <summary>
/// Registers the AgentAccounts module: <see cref="IAgentAccountsApi"/> and its database.
/// </summary>
public static class AgentAccountsModule
{
    /// <summary>Registers the module. The host also registers a <see cref="System.TimeProvider"/> and the Workspaces module.</summary>
    public static IServiceCollection AddAgentAccountsModule(this IServiceCollection services, AgentAccountsSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton(new SecretBox(settings.EncryptionKey));
        services.AddModuleDbContext<AgentAccountsDbContext>(settings.ConnectionString, AgentAccountsDbContext.Schema);
        services.AddScoped<IAgentAccountsApi, AgentAccountsApi>();
        return services;
    }
}
