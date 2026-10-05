using Bagatka.AiSloth.Sources.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Sources.Data;

internal sealed class RepositoryConfiguration : IEntityTypeConfiguration<Repository>
{
    public void Configure(EntityTypeBuilder<Repository> builder)
    {
        builder.ToTable("repositories");
        builder.HasKey(repository => repository.Id);
        builder.Property(repository => repository.Owner).HasMaxLength(100);
        builder.Property(repository => repository.Name).HasMaxLength(100);
        builder.Property(repository => repository.DefaultBranch).HasMaxLength(255);

        // A name is a folder in the workspace's nooks, so it is the workspace's once; lists them.
        builder.HasIndex(repository => new { repository.WorkspaceId, repository.Name }).IsUnique();
    }
}
