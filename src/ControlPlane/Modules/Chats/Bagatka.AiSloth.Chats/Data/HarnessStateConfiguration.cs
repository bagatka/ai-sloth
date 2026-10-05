using Bagatka.AiSloth.Chats.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Chats.Data;

internal sealed class HarnessStateConfiguration : IEntityTypeConfiguration<HarnessState>
{
    public void Configure(EntityTypeBuilder<HarnessState> builder)
    {
        builder.ToTable("harness_states");
        builder.HasKey(state => new { state.PersonId, state.WorkspaceId, state.Harness });
        builder.Property(state => state.Harness).HasMaxLength(Chat.MaxHarnessLength);
        builder.Property(state => state.Version).IsRowVersion();
        builder.Ignore(state => state.ObjectKey);
        builder.Ignore(state => state.VersionFolder);
        builder.Ignore(state => state.Folder);
    }
}
