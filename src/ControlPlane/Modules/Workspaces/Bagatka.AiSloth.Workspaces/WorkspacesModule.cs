using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.AiSloth.Workspaces.Data;
using Bagatka.Foundation.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.Workspaces;

/// <summary>
/// Registers the Workspaces module: <see cref="IWorkspacesApi"/> and its database.
/// </summary>
public static class WorkspacesModule
{
    /// <summary>Registers the module. The host must also register a <see cref="System.TimeProvider"/>.</summary>
    public static IServiceCollection AddWorkspacesModule(this IServiceCollection services)
    {
        services.AddModuleDbContext<WorkspacesDbContext>(WorkspacesDbContext.Schema);
        services.AddSingleton<IWorkspacesApi, WorkspacesApi>();
        return services;
    }
}
