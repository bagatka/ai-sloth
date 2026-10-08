using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bagatka.Foundation.Modules.Data;

// For `dotnet ef migrations add` only. Generating a migration never connects to a database.
internal sealed class InstanceLeaseDbContextFactory : IDesignTimeDbContextFactory<InstanceLeaseDbContext>
{
    public InstanceLeaseDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<InstanceLeaseDbContext> options = new DbContextOptionsBuilder<InstanceLeaseDbContext>();
        options.UseModuleDatabase(database: null, InstanceLeaseDbContext.Schema);
        return new InstanceLeaseDbContext(options.Options);
    }
}
