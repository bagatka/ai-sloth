using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Bagatka.AiSloth.Chats.Data;

// For `dotnet ef migrations add` only. Generating a migration never connects to a database.
internal sealed class ChatsDbContextFactory : IDesignTimeDbContextFactory<ChatsDbContext>
{
    public ChatsDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<ChatsDbContext> options = new DbContextOptionsBuilder<ChatsDbContext>();
        options.UseModuleDatabase(database: null, ChatsDbContext.Schema);
        return new ChatsDbContext(options.Options);
    }
}
