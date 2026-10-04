using Bagatka.AiSloth.Workspaces.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Workspaces.Data;

internal sealed class ResourceLinkConfiguration : IEntityTypeConfiguration<ResourceLink>
{
    public void Configure(EntityTypeBuilder<ResourceLink> builder)
    {
        // A child may later be in several parents, such as a nook in several projects.
        builder.HasKey(link => new { link.ChildId, link.ParentId });
    }
}
