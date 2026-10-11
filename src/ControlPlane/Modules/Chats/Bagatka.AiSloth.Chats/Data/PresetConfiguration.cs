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
        builder.Property(preset => preset.Thinking).HasMaxLength(Chat.MaxThinkingLength);

        // A person's presets, each harness, account, model, and thinking level once, the harness's
        // defaults too.
        builder.HasIndex(preset => new { preset.OwnerId, preset.Harness, preset.AgentAccountId, preset.Model, preset.Thinking }).IsUnique().AreNullsDistinct(false);
    }
}
