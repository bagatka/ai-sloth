using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Sources.Data;

internal sealed class SourcesDbContext(DbContextOptions<SourcesDbContext> options) : DbContext(options)
{
    public const string Schema = "sources";

    public DbSet<GitHubConnection> GitHubConnections => Set<GitHubConnection>();

    public DbSet<GitHubConnectionAttempt> GitHubConnectionAttempts => Set<GitHubConnectionAttempt>();

    public DbSet<Repository> Repositories => Set<Repository>();

    public DbSet<PersonGitSettings> GitSettings => Set<PersonGitSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfiguration(new GitHubConnectionConfiguration());
        modelBuilder.ApplyConfiguration(new GitHubConnectionAttemptConfiguration());
        modelBuilder.ApplyConfiguration(new RepositoryConfiguration());
        modelBuilder.ApplyConfiguration(new PersonGitSettingsConfiguration());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<RepositoryId>().HaveConversion<TypedIdConverter<RepositoryId>>();
        configurationBuilder.Properties<GitHubConnectionAttemptId>().HaveConversion<TypedIdConverter<GitHubConnectionAttemptId>>();
        configurationBuilder.Properties<WorkspaceId>().HaveConversion<TypedIdConverter<WorkspaceId>>();
        configurationBuilder.Properties<UserId>().HaveConversion<TypedIdConverter<UserId>>();
    }
}
