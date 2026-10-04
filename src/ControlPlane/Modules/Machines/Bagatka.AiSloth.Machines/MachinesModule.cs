using Bagatka.AiSloth.Machines.Connections;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Machines.Data;
using Bagatka.Foundation.Modules;
using Bagatka.Sandboxing;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.Machines;

/// <summary>
/// Registers the Machines module: <see cref="IMachinesApi"/>, <see cref="IMachineConnectionsApi"/>, their
/// database, the connections this instance holds, and the <c>machine</c> sandbox provider.
/// </summary>
public static class MachinesModule
{
    /// <summary>Registers the module. The host also registers a <see cref="System.TimeProvider"/> and the Workspaces module.</summary>
    public static IServiceCollection AddMachinesModule(this IServiceCollection services, MachinesSettings settings)
    {
        services.AddModuleDbContext<MachinesDbContext>(settings.ConnectionString, MachinesDbContext.Schema);
        services.AddSingleton<MachineConnections>();
        services.AddSingleton<ISandboxProvider, MachineSandboxProvider>();
        services.AddScoped<MachinesApi>();
        services.AddScoped<IMachinesApi>(provider => provider.GetRequiredService<MachinesApi>());
        services.AddScoped<IMachineConnectionsApi>(provider => provider.GetRequiredService<MachinesApi>());
        return services;
    }
}
