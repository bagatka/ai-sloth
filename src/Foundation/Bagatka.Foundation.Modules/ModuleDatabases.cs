using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Foundation.Modules;

/// <summary>
/// Applies the migrations of every module registered with
/// <see cref="ModuleDatabaseRegistration.AddModuleDbContext"/> (PATTERNS.md, entry 14).
/// </summary>
public static class ModuleDatabases
{
    /// <summary>
    /// Brings every module's schema up to date, one module after another. Safe to run from several
    /// processes at once: EF Core holds a database lock while it migrates.
    /// </summary>
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken ct)
    {
        foreach (ModuleDatabase database in services.GetServices<ModuleDatabase>())
        {
            await database.MigrateAsync(services, ct);
        }
    }
}
