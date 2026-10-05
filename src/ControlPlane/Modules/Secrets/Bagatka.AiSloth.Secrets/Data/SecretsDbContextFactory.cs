using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bagatka.AiSloth.Secrets.Data;

// For `dotnet ef migrations add` only. Generating a migration never connects to a database.
internal sealed class SecretsDbContextFactory : IDesignTimeDbContextFactory<SecretsDbContext>
{
    public SecretsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<SecretsDbContext> options = new DbContextOptionsBuilder<SecretsDbContext>();
        options.UseModuleDatabase("Host=localhost", SecretsDbContext.Schema);
        return new SecretsDbContext(options.Options);
    }
}
