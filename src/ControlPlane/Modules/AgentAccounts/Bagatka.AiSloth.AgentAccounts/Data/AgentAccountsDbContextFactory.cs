using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bagatka.AiSloth.AgentAccounts.Data;

// For `dotnet ef migrations add` only. Generating a migration never connects to a database.
internal sealed class AgentAccountsDbContextFactory : IDesignTimeDbContextFactory<AgentAccountsDbContext>
{
    public AgentAccountsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<AgentAccountsDbContext> options = new DbContextOptionsBuilder<AgentAccountsDbContext>();
        options.UseModuleDatabase("Host=localhost", AgentAccountsDbContext.Schema);
        return new AgentAccountsDbContext(options.Options);
    }
}
