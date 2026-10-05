using Bagatka.AiSloth.Secrets.Contracts;
using Bagatka.AiSloth.Secrets.Data;
using Bagatka.Foundation.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.Secrets;

/// <summary>
/// Registers the Secrets module: <see cref="ISecretsApi"/> and its database.
/// </summary>
public static class SecretsModule
{
    /// <summary>Registers the module. The host also registers a <see cref="System.TimeProvider"/> and the Workspaces module.</summary>
    public static IServiceCollection AddSecretsModule(this IServiceCollection services, SecretsSettings settings)
    {
        services.AddSingleton(settings);
        services.AddKeyedSingleton(SecretsDbContext.Schema, new SecretBox(settings.EncryptionKey));
        services.AddModuleDbContext<SecretsDbContext>(settings.ConnectionString, SecretsDbContext.Schema);
        services.AddScoped<ISecretsApi, SecretsApi>();
        return services;
    }
}
