using Bagatka.AiSloth.Nooks.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Nooks.Data;

internal sealed class SourceCopyConfiguration : IEntityTypeConfiguration<SourceCopy>
{
    public void Configure(EntityTypeBuilder<SourceCopy> builder)
    {
        builder.ToTable("source_copies");

        // A nook's sources by name, which are its folders in /work.
        builder.HasKey(copy => new { copy.NookId, copy.Name });
        builder.Property(copy => copy.Name).HasMaxLength(100);
        builder.Property(copy => copy.Branch).HasMaxLength(255);
        builder.Property(copy => copy.Commit).HasMaxLength(64);
        builder.Ignore(copy => copy.CopiedIn);
    }
}
