using Bagatka.AiSloth.Nooks.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Nooks.Data;

internal sealed class CheckpointConfiguration : IEntityTypeConfiguration<Checkpoint>
{
    public void Configure(EntityTypeBuilder<Checkpoint> builder)
    {
        builder.HasKey(checkpoint => checkpoint.Id);
        builder.Property(checkpoint => checkpoint.Note).HasMaxLength(Checkpoint.MaxNoteLength);

        // A nook's checkpoints by number, and its pages, newest first.
        builder.HasIndex(checkpoint => new { checkpoint.NookId, checkpoint.Number }).IsUnique();
        builder.HasIndex(checkpoint => new { checkpoint.NookId, checkpoint.Id });
    }
}
