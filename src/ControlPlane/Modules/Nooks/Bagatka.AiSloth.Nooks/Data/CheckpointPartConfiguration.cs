using Bagatka.AiSloth.Nooks.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Nooks.Data;

internal sealed class CheckpointPartConfiguration : IEntityTypeConfiguration<CheckpointPart>
{
    public void Configure(EntityTypeBuilder<CheckpointPart> builder)
    {
        builder.ToTable("checkpoint_parts");
        builder.HasKey(part => new { part.CheckpointId, part.Path });
        builder.Property(part => part.Path).HasMaxLength(CheckpointPart.MaxPathLength);
        builder.Property(part => part.Commit).HasMaxLength(64);
        builder.Property(part => part.Previous).HasMaxLength(64);
        builder.Property(part => part.ObjectKey).HasMaxLength(512);
        builder.HasOne<Checkpoint>().WithMany().HasForeignKey(part => part.CheckpointId).OnDelete(DeleteBehavior.Cascade);
    }
}
