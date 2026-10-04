using Bagatka.AiSloth.Nooks.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Nooks.Data;

internal sealed class NookConfiguration : IEntityTypeConfiguration<Nook>
{
    public void Configure(EntityTypeBuilder<Nook> builder)
    {
        builder.HasKey(nook => nook.Id);
        builder.Property(nook => nook.Provider).HasMaxLength(Nook.MaxProviderLength);
        builder.Property(nook => nook.Location).HasMaxLength(Nook.MaxLocationLength);
        builder.Property(nook => nook.Version).IsRowVersion();

        // Lists a workspace's nooks, and finds the nooks the reconciler works on.
        builder.HasIndex(nook => new { nook.WorkspaceId, nook.Id });
        builder.HasIndex(nook => new { nook.Status, nook.Id });
    }
}
