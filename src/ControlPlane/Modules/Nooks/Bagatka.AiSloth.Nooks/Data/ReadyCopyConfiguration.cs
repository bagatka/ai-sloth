using Bagatka.AiSloth.Nooks.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Nooks.Data;

internal sealed class ReadyCopyConfiguration : IEntityTypeConfiguration<ReadyCopy>
{
    public void Configure(EntityTypeBuilder<ReadyCopy> builder)
    {
        builder.HasKey(copy => copy.Match);
        builder.Property(copy => copy.Match).HasMaxLength(ReadyCopy.MatchLength).IsFixedLength();
        builder.Property(copy => copy.Provider).HasMaxLength(Nook.MaxProviderLength);
        builder.Property(copy => copy.Location).HasMaxLength(Nook.MaxLocationLength);
        builder.Property(copy => copy.Version).IsRowVersion();

        // Copies nobody started from for long, oldest first.
        builder.HasIndex(copy => copy.UsedAt);
    }
}
