using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.AiSloth.Workspaces.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Workspaces.Data;

internal sealed class WorkspacesDbContext(DbContextOptions<WorkspacesDbContext> options) : DbContext(options)
{
    public const string Schema = "workspaces";

    public DbSet<Workspace> Workspaces => Set<Workspace>();

    public DbSet<Grant> Grants => Set<Grant>();

    public DbSet<ResourceLink> Links => Set<ResourceLink>();

    public DbSet<StoredInvite> Invites => Set<StoredInvite>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new WorkspaceConfiguration());
        modelBuilder.ApplyConfiguration(new GrantConfiguration());
        modelBuilder.ApplyConfiguration(new ResourceLinkConfiguration());
        modelBuilder.ApplyConfiguration(new StoredInviteConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<WorkspaceId>().HaveConversion<TypedIdConverter<WorkspaceId>>();
        configurationBuilder.Properties<UserId>().HaveConversion<TypedIdConverter<UserId>>();
        configurationBuilder.Properties<WorkspaceName>()
            .HaveConversion<WorkspaceNameConverter>()
            .HaveMaxLength(WorkspaceName.MaxLength);
        configurationBuilder.Properties<AccessLevel>().HaveConversion<string>().HaveMaxLength(StoredEnums.MaxLength);
        configurationBuilder.Properties<ResourceKind>().HaveConversion<string>().HaveMaxLength(StoredEnums.MaxLength);
    }
}
