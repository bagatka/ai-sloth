using Bagatka.AiSloth.Chats.Harness;
using Bagatka.AiSloth.Chats.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Chats.Data;

internal sealed class ChatConfiguration : IEntityTypeConfiguration<Chat>
{
    public void Configure(EntityTypeBuilder<Chat> builder)
    {
        builder.HasKey(chat => chat.Id);
        builder.Property(chat => chat.Version).IsRowVersion();
        builder.Property(chat => chat.SessionId).HasMaxLength(256);
        builder.Property(chat => chat.ResumableSessionId).HasMaxLength(256);
        builder.Property(chat => chat.HarnessStateFiles).HasMaxLength(StateFiles.MaxManifestLength);
        builder.Property(chat => chat.Harness).HasMaxLength(Chat.MaxHarnessLength);

        // One chat per nook: its agent is the only one working on the nook's files. Lists a
        // workspace's chats, and finds the chat a model call's token belongs to.
        builder.HasIndex(chat => chat.NookId).IsUnique();
        builder.HasIndex(chat => new { chat.WorkspaceId, chat.Id });
        builder.HasIndex(chat => chat.HarnessTokenHash).IsUnique();
    }
}
