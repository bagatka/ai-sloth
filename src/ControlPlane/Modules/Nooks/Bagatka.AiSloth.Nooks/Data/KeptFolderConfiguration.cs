using Bagatka.AiSloth.Nooks.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Nooks.Data;

internal sealed class KeptFolderConfiguration : IEntityTypeConfiguration<KeptFolder>
{
    public void Configure(EntityTypeBuilder<KeptFolder> builder)
    {
        builder.HasKey(folder => folder.Name);
        builder.Property(folder => folder.Name).HasMaxLength(KeptFolder.MaxNameLength);
        builder.Property(folder => folder.Head).HasMaxLength(64);
        builder.Property(folder => folder.Version).IsRowVersion();
    }
}
