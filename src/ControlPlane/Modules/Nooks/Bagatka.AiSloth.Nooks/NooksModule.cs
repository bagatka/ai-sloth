using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Jobs;
using Bagatka.Foundation.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.Nooks;

/// <summary>
/// Registers the Nooks module: <see cref="INooksApi"/>, <see cref="INookDaemonsApi"/>, their database,
/// the daemon connections this instance holds, and the reconciler.
/// </summary>
public static class NooksModule
{
    /// <summary>
    /// Registers the module. The host also registers a <see cref="System.TimeProvider"/>, the
    /// Workspaces, Machines, Secrets, and Sources modules, every <see cref="Bagatka.Sandboxing.ISandboxProvider"/>
    /// nooks may run on, and the <see cref="Bagatka.ObjectStorage.IObjectStorage"/> that keeps checkpoints.
    /// </summary>
    public static IServiceCollection AddNooksModule(this IServiceCollection services, NooksSettings settings)
    {
        services.AddSingleton(settings);
        services.AddModuleDbContext<NooksDbContext>(settings.ConnectionString, NooksDbContext.Schema);
        services.AddSingleton<DaemonConnections>();
        services.AddSingleton<InputFeeds>();
        services.AddSingleton<FileLocks>();
        services.AddSingleton<ReadyCopies>();
        services.AddSingleton<NookReconciler>();
        services.AddHostedService(provider => provider.GetRequiredService<NookReconciler>());
        services.AddSingleton<NookActivity>();
        services.AddSingleton<NookSleeper>();
        services.AddHostedService(provider => provider.GetRequiredService<NookSleeper>());
        services.AddScoped<NooksApi>();
        services.AddScoped<INooksApi>(provider => provider.GetRequiredService<NooksApi>());
        services.AddScoped<INookDaemonsApi>(provider => provider.GetRequiredService<NooksApi>());
        return services;
    }
}
