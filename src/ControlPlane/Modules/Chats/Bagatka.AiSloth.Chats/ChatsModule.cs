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
    /// Registers the module. The host also registers a <see cref="System.TimeProvider"/> and the
    /// Workspaces and Nooks modules.
    /// </summary>
    public static IServiceCollection AddChatsModule(this IServiceCollection services, ChatsSettings settings)
    {
        services.AddSingleton(settings);
        services.AddModuleDbContext<ChatsDbContext>(settings.ConnectionString, ChatsDbContext.Schema);
        services.AddSingleton<ChatSignals>();
        services.AddSingleton<ChatRunners>();
        services.AddHostedService(provider => provider.GetRequiredService<ChatRunners>());
        services.AddScoped<ChatsApi>();
        services.AddScoped<IChatsApi>(provider => provider.GetRequiredService<ChatsApi>());
        services.AddScoped<IChatHarnessesApi>(provider => provider.GetRequiredService<ChatsApi>());
        return services;
    }
}
