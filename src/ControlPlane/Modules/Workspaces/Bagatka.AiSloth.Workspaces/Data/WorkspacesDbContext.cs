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

    public DbSet<Member> Members => Set<Member>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new WorkspaceConfiguration());
        modelBuilder.ApplyConfiguration(new MemberConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<WorkspaceId>().HaveConversion<TypedIdConverter<WorkspaceId>>();
        configurationBuilder.Properties<UserId>().HaveConversion<TypedIdConverter<UserId>>();
        configurationBuilder.Properties<WorkspaceName>()
            .HaveConversion<WorkspaceNameConverter>()
            .HaveMaxLength(WorkspaceName.MaxLength);
        configurationBuilder.Properties<WorkspaceRole>().HaveConversion<string>().HaveMaxLength(StoredEnums.MaxLength);
    }
}
