using Bagatka.AiSloth.Sources.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Sources.Data;

internal sealed class GitHubConnectionConfiguration : IEntityTypeConfiguration<GitHubConnection>
{
    public void Configure(EntityTypeBuilder<GitHubConnection> builder)
    {
        builder.ToTable("github_connections");

        // One GitHub account per person.
        builder.HasKey(connection => connection.UserId);
        builder.Property(connection => connection.Login).HasMaxLength(100);
        builder.Property(connection => connection.Name).HasMaxLength(255);
        builder.Property(connection => connection.Version).IsRowVersion();
    }
}
