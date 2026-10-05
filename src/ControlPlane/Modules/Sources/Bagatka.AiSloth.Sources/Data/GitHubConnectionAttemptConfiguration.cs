using Bagatka.AiSloth.Sources.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Sources.Data;

internal sealed class GitHubConnectionAttemptConfiguration : IEntityTypeConfiguration<GitHubConnectionAttempt>
{
    public void Configure(EntityTypeBuilder<GitHubConnectionAttempt> builder)
    {
        builder.ToTable("github_connection_attempts");
        builder.HasKey(attempt => attempt.Id);

        // Clears a person's expired attempts.
        builder.HasIndex(attempt => new { attempt.UserId, attempt.ExpiresAt });
    }
}
