using Bagatka.AiSloth.Chats.Model;
using Bagatka.Harnesses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Chats.Data;

internal sealed class PresetConfiguration : IEntityTypeConfiguration<Preset>
{
    public void Configure(EntityTypeBuilder<Preset> builder)
    {
        builder.ToTable("presets");
        builder.HasKey(preset => preset.Id);
        builder.Property(preset => preset.Name).HasMaxLength(Preset.MaxNameLength);
        builder.Property(preset => preset.Harness).HasMaxLength(Chat.MaxHarnessLength);
        builder.Property(preset => preset.Model).HasMaxLength(HarnessProfile.MaxModelLength);

        // A person's presets, each harness, account, and model once: the harness's default model too.
        builder.HasIndex(preset => new { preset.OwnerId, preset.Harness, preset.AgentAccountId, preset.Model }).IsUnique().AreNullsDistinct(false);
    }
}
