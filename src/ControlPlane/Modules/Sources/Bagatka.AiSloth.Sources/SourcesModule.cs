using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Data;
using Bagatka.Foundation.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.Sources;

/// <summary>
/// Registers the Sources module: <see cref="ISourcesApi"/> and its database.
/// </summary>
public static class SourcesModule
{
    /// <summary>
    /// Registers the module. The host also registers a <see cref="System.TimeProvider"/>, the
    /// Workspaces module, and a <c>GitHubClient</c> (<c>AddGitHubClient</c>). Copying and pushing run
    /// the <c>git</c> command line, which the host's computer has.
    /// </summary>
    public static IServiceCollection AddSourcesModule(this IServiceCollection services, SourcesSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton<CoAuthorLine>();
        services.AddKeyedSingleton(SourcesDbContext.Schema, new SecretBox(settings.EncryptionKey));
        services.AddModuleDbContext<SourcesDbContext>(settings.ConnectionString, SourcesDbContext.Schema);
        services.AddScoped<ISourcesApi, SourcesApi>();
        return services;
    }
}
