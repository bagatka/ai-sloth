using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bagatka.AiSloth.Machines.Data;

// For `dotnet ef migrations add` only. Generating a migration never connects to a database.
internal sealed class MachinesDbContextFactory : IDesignTimeDbContextFactory<MachinesDbContext>
{
    public MachinesDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<MachinesDbContext> options = new DbContextOptionsBuilder<MachinesDbContext>();
        options.UseModuleDatabase("Host=localhost", MachinesDbContext.Schema);
        return new MachinesDbContext(options.Options);
    }
}
