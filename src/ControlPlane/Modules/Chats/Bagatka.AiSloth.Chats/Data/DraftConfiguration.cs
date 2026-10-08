using Bagatka.AiSloth.Chats.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Chats.Data;

internal sealed class DraftConfiguration : IEntityTypeConfiguration<Draft>
{
    public void Configure(EntityTypeBuilder<Draft> builder)
    {
        builder.HasKey(draft => draft.ChatId);

        // Drafts nobody wrote in for long.
        builder.HasIndex(draft => draft.StartedAt);
    }
}
