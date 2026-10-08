using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bagatka.AiSloth.Nooks.Data;

// For `dotnet ef migrations add` only. Generating a migration never connects to a database.
internal sealed class NooksDbContextFactory : IDesignTimeDbContextFactory<NooksDbContext>
{
    public NooksDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<NooksDbContext> options = new DbContextOptionsBuilder<NooksDbContext>();
        options.UseModuleDatabase(database: null, NooksDbContext.Schema);
        return new NooksDbContext(options.Options);
    }
}
