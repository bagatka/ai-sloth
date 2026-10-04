using Bagatka.AiSloth.Users.Contracts;
using Bagatka.AiSloth.Users.Data;
using Bagatka.Foundation.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.Users;

/// <summary>
/// Registers the Users module: <see cref="IUsersApi"/> and its database.
/// </summary>
public static class UsersModule
{
    /// <summary>Registers the module. The host must also register a <see cref="System.TimeProvider"/>.</summary>
    public static IServiceCollection AddUsersModule(this IServiceCollection services, UsersSettings settings)
    {
        services.AddModuleDbContext<UsersDbContext>(settings.ConnectionString, UsersDbContext.Schema);
        services.AddScoped<IUsersApi, UsersApi>();
        return services;
    }
}
