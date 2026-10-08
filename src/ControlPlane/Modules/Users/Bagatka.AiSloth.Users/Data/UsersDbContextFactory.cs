using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bagatka.AiSloth.Users.Data;

// For `dotnet ef migrations add` only. Generating a migration never connects to a database.
internal sealed class UsersDbContextFactory : IDesignTimeDbContextFactory<UsersDbContext>
{
    public UsersDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<UsersDbContext> options = new DbContextOptionsBuilder<UsersDbContext>();
        options.UseModuleDatabase(database: null, UsersDbContext.Schema);
        return new UsersDbContext(options.Options);
    }
}
