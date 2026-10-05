using Bagatka.AiSloth.Secrets.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Secrets.Data;

internal sealed class SecretConfiguration : IEntityTypeConfiguration<Secret>
{
    public void Configure(EntityTypeBuilder<Secret> builder)
    {
        builder.ToTable("secrets");
        builder.HasKey(secret => secret.Id);

        // One value per name in a workspace; lists and resolves a workspace's secrets.
        builder.HasIndex(secret => new { secret.WorkspaceId, secret.Name }).IsUnique();
    }
}
