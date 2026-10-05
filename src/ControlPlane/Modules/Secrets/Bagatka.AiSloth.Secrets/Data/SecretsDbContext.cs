using Bagatka.AiSloth.Secrets.Contracts;
using Bagatka.AiSloth.Secrets.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Secrets.Data;

internal sealed class SecretsDbContext(DbContextOptions<SecretsDbContext> options) : DbContext(options)
{
    public const string Schema = "secrets";

    public DbSet<Secret> Secrets => Set<Secret>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new SecretConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<SecretId>().HaveConversion<TypedIdConverter<SecretId>>();
        configurationBuilder.Properties<WorkspaceId>().HaveConversion<TypedIdConverter<WorkspaceId>>();
        configurationBuilder.Properties<UserId>().HaveConversion<TypedIdConverter<UserId>>();
        configurationBuilder.Properties<SecretName>()
            .HaveConversion<SecretNameConverter>()
            .HaveMaxLength(SecretName.MaxLength);
    }
}
