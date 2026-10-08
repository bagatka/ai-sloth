using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Harness;
using Bagatka.Foundation.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.Chats;

/// <summary>
/// Registers the Chats module: <see cref="IChatsApi"/>, <see cref="IChatHarnessesApi"/>, their
/// database, and the runners that talk to agents.
/// </summary>
public static class ChatsModule
{
    /// <summary>
    /// Registers the module. The host also registers a <see cref="System.TimeProvider"/>, and the
    /// Workspaces, Nooks, and AgentAccounts modules.
    /// </summary>
    public static IServiceCollection AddChatsModule(this IServiceCollection services, ChatsSettings settings)
    {
        services.AddSingleton(settings);
        services.AddModuleDbContext<ChatsDbContext>(ChatsDbContext.Schema);
        services.AddSingleton<ChatSignals>();
        services.AddSingleton<AgentProcess>();
        services.AddSingleton<HarnessStates>();
        services.AddSingleton<AgentInstructions>();
        services.AddSingleton<ChatsMeter>();
        services.AddSingleton<NookSetups>();
        services.AddSingleton<ChatRunners>();
        services.AddSingleton<Drafts>();
        services.AddHostedService(provider => provider.GetRequiredService<Drafts>());
        services.AddHostedService(provider => provider.GetRequiredService<ChatRunners>());
        services.AddSingleton<ChatsApi>();
        services.AddSingleton<IChatsApi>(provider => provider.GetRequiredService<ChatsApi>());
        services.AddSingleton<IChatHarnessesApi>(provider => provider.GetRequiredService<ChatsApi>());
        return services;
    }
}
