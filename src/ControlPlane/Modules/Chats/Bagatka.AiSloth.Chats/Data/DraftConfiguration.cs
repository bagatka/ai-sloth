using Bagatka.AiSloth.Chats.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Chats.Data;

internal sealed class DraftConfiguration : IEntityTypeConfiguration<Draft>
{
    public void Configure(EntityTypeBuilder<Draft> builder)
    {
        builder.HasKey(draft => draft.ChatId);

        // A person's drafts in a workspace, newest first, and drafts nobody wrote in for long.
        builder.HasIndex(draft => new { draft.StartedBy, draft.WorkspaceId, draft.StartedAt });
        builder.HasIndex(draft => draft.StartedAt);
    }
}
