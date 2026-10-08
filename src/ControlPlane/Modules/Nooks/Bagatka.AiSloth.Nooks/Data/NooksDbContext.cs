using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks.Data;

internal sealed class NooksDbContext(DbContextOptions<NooksDbContext> options) : DbContext(options)
{
    public const string Schema = "nooks";

    public DbSet<Nook> Nooks => Set<Nook>();

    public DbSet<Process> Processes => Set<Process>();

    public DbSet<SourceCopy> SourceCopies => Set<SourceCopy>();

    public DbSet<Checkpoint> Checkpoints => Set<Checkpoint>();

    public DbSet<CheckpointPart> CheckpointParts => Set<CheckpointPart>();

    public DbSet<ReadyCopy> ReadyCopies => Set<ReadyCopy>();

    public DbSet<KeptFolder> KeptFolders => Set<KeptFolder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new NookConfiguration());
        modelBuilder.ApplyConfiguration(new ProcessConfiguration());
        modelBuilder.ApplyConfiguration(new SourceCopyConfiguration());
        modelBuilder.ApplyConfiguration(new CheckpointConfiguration());
        modelBuilder.ApplyConfiguration(new CheckpointPartConfiguration());
        modelBuilder.ApplyConfiguration(new ReadyCopyConfiguration());
        modelBuilder.ApplyConfiguration(new KeptFolderConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<NookId>().HaveConversion<TypedIdConverter<NookId>>();
        configurationBuilder.Properties<ProcessId>().HaveConversion<TypedIdConverter<ProcessId>>();
        configurationBuilder.Properties<CheckpointId>().HaveConversion<TypedIdConverter<CheckpointId>>();
        configurationBuilder.Properties<WorkspaceId>().HaveConversion<TypedIdConverter<WorkspaceId>>();
        configurationBuilder.Properties<RepositoryId>().HaveConversion<TypedIdConverter<RepositoryId>>();
        configurationBuilder.Properties<UserId>().HaveConversion<TypedIdConverter<UserId>>();
        configurationBuilder.Properties<NookStatus>().HaveConversion<string>().HaveMaxLength(StoredEnums.MaxLength);
        configurationBuilder.Properties<OutputRetention>().HaveConversion<string>().HaveMaxLength(StoredEnums.MaxLength);
    }
}
