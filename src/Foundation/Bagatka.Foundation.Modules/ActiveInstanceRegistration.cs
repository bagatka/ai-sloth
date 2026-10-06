using System;
using Bagatka.Foundation.Modules.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bagatka.Foundation.Modules;

/// <summary>Registers <see cref="ActiveInstance"/>, the lease that decides which instance runs background work.</summary>
public static class ActiveInstanceRegistration
{
    /// <summary>
    /// Adds the instance lease, kept in the shared database (its migrations apply with every module's),
    /// and starts asking for it with the host. A host with background work calls it once.
    /// </summary>
    public static IServiceCollection AddActiveInstance(this IServiceCollection services, string connectionString)
    {
        services.AddModuleDbContext<InstanceLeaseDbContext>(connectionString, InstanceLeaseDbContext.Schema);
        services.AddSingleton(provider => new ActiveInstance(
            provider.GetRequiredService<IDbContextFactory<InstanceLeaseDbContext>>(),
            provider.GetRequiredService<IHostApplicationLifetime>(),
            provider.GetRequiredService<TimeProvider>(),
            provider.GetRequiredService<ILogger<ActiveInstance>>()));
        services.AddHostedService(provider => provider.GetRequiredService<ActiveInstance>());
        return services;
    }
}
