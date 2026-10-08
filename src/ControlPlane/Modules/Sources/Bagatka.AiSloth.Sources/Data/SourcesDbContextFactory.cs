using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bagatka.AiSloth.Sources.Data;

// For `dotnet ef migrations add` only. Generating a migration never connects to a database.
internal sealed class SourcesDbContextFactory : IDesignTimeDbContextFactory<SourcesDbContext>
{
    public SourcesDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<SourcesDbContext> options = new DbContextOptionsBuilder<SourcesDbContext>();
        options.UseModuleDatabase(database: null, SourcesDbContext.Schema);
        return new SourcesDbContext(options.Options);
    }
}
