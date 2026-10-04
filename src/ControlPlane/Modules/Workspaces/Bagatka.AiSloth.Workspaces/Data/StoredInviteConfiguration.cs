using Bagatka.AiSloth.Workspaces.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Workspaces.Data;

internal sealed class StoredInviteConfiguration : IEntityTypeConfiguration<StoredInvite>
{
    public void Configure(EntityTypeBuilder<StoredInvite> builder)
    {
        builder.ToTable("invites");
        builder.HasKey(invite => invite.Id);
        builder.Property(invite => invite.Version).IsRowVersion();
        builder.Ignore(invite => invite.Resource);

        // Finds the invite a code belongs to.
        builder.HasIndex(invite => invite.CodeHash).IsUnique();
    }
}
