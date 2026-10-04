using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks.Data;

internal sealed class NooksDbContext(DbContextOptions<NooksDbContext> options) : DbContext(options)
{
    public const string Schema = "nooks";

    public DbSet<Nook> Nooks => Set<Nook>();

    public DbSet<Process> Processes => Set<Process>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new NookConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<NookId>().HaveConversion<TypedIdConverter<NookId>>();
        configurationBuilder.Properties<ProcessId>().HaveConversion<TypedIdConverter<ProcessId>>();
        configurationBuilder.Properties<WorkspaceId>().HaveConversion<TypedIdConverter<WorkspaceId>>();
        configurationBuilder.Properties<NookStatus>().HaveConversion<string>().HaveMaxLength(StoredEnums.MaxLength);
        configurationBuilder.Properties<OutputRetention>().HaveConversion<string>().HaveMaxLength(StoredEnums.MaxLength);
    }
}
