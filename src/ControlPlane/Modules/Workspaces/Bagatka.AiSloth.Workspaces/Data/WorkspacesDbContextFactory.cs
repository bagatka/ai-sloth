using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bagatka.AiSloth.Workspaces.Data;

// For `dotnet ef migrations add` only. Generating a migration never connects to a database.
internal sealed class WorkspacesDbContextFactory : IDesignTimeDbContextFactory<WorkspacesDbContext>
{
    public WorkspacesDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<WorkspacesDbContext> options = new DbContextOptionsBuilder<WorkspacesDbContext>();
        options.UseModuleDatabase(database: null, WorkspacesDbContext.Schema);
        return new WorkspacesDbContext(options.Options);
    }
}
