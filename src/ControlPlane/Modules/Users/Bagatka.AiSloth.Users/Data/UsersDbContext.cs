using Bagatka.AiSloth.Users.Contracts;
using Bagatka.AiSloth.Users.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Users.Data;

internal sealed class UsersDbContext(DbContextOptions<UsersDbContext> options) : DbContext(options)
{
    public const string Schema = "users";

    public DbSet<User> Users => Set<User>();

    public DbSet<Session> Sessions => Set<Session>();

    public DbSet<IssuedCode> IssuedCodes => Set<IssuedCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new SessionConfiguration());
        modelBuilder.ApplyConfiguration(new IssuedCodeConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<UserId>().HaveConversion<TypedIdConverter<UserId>>();
        configurationBuilder.Properties<SessionId>().HaveConversion<TypedIdConverter<SessionId>>();
        configurationBuilder.Properties<IssuedCodePurpose>().HaveConversion<string>().HaveMaxLength(StoredEnums.MaxLength);
        configurationBuilder.Properties<BoundedName>()
            .HaveConversion<BoundedNameConverter>()
            .HaveMaxLength(BoundedName.MaxLength);
    }
}
