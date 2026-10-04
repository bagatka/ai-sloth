using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Foundation.Modules;

/// <summary>
/// Puts a module's <see cref="DbContext"/> on the shared PostgreSQL database, in the module's own
/// schema (PATTERNS.md, entry 13).
/// </summary>
public static class ModuleDatabaseRegistration
{
    /// <summary>
    /// Registers <typeparamref name="TContext"/> as a scoped service, plus an
    /// <see cref="IDbContextFactory{TContext}"/> for work that outlives a request.
    /// <see cref="ModuleDatabases.MigrateAsync"/> applies its migrations.
    /// </summary>
    public static IServiceCollection AddModuleDbContext<TContext>(this IServiceCollection services, string connectionString, string schema)
        where TContext : DbContext
    {
        services.AddDbContextFactory<TContext>(options => options.UseModuleDatabase(connectionString, schema));
        services.AddSingleton(new ModuleDatabase(typeof(TContext)));
        return services;
    }

    /// <summary>
    /// The options every module context uses: PostgreSQL, snake_case names, and the migrations
    /// history table in the module's schema. Each module's design-time factory calls it too, so
    /// migrations are generated against the same model.
    /// </summary>
    public static DbContextOptionsBuilder UseModuleDatabase(this DbContextOptionsBuilder options, string connectionString, string schema)
    {
        return options
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, schema))
            .UseSnakeCaseNamingConvention();
    }
}
