using Bagatka.AiSloth.Workspaces.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Workspaces.Data;

internal sealed class GrantConfiguration : IEntityTypeConfiguration<Grant>
{
    public void Configure(EntityTypeBuilder<Grant> builder)
    {
        // One grant per resource and person.
        builder.HasKey(grant => new { grant.ResourceId, grant.UserId });

        // Finds a person's access, and lists their workspaces.
        builder.HasIndex(grant => new { grant.UserId, grant.ResourceId });
    }
}
