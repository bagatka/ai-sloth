using Bagatka.AiSloth.Chats.Model;
using Bagatka.Harnesses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Chats.Data;

internal sealed class ChatConfiguration : IEntityTypeConfiguration<Chat>
{
    public void Configure(EntityTypeBuilder<Chat> builder)
    {
        builder.HasKey(chat => chat.Id);
        builder.Property(chat => chat.Version).IsRowVersion();
        builder.ComplexProperty(chat => chat.Session, session =>
        {
            session.Property(value => value.SessionId).HasColumnName("session_id").HasMaxLength(256);
            session.Property(value => value.Earlier).HasColumnName("earlier_session_id").HasMaxLength(256);
            session.Property(value => value.CanSteer).HasColumnName("can_steer");
            session.Property(value => value.Loading).HasColumnName("loading_session");
        });
        builder.Property(chat => chat.Harness).HasMaxLength(Chat.MaxHarnessLength);
        builder.Property(chat => chat.Model).HasMaxLength(HarnessProfile.MaxModelLength);
        builder.Property(chat => chat.NextModel).HasMaxLength(HarnessProfile.MaxModelLength);
        builder.Property(chat => chat.Thinking).HasMaxLength(Chat.MaxThinkingLength);
        builder.Property(chat => chat.NextThinking).HasMaxLength(Chat.MaxThinkingLength);

        // One chat per nook: its agent is the only one working on the nook's files. Lists a
        // workspace's chats, and finds the chat a model call's token belongs to.
        builder.HasIndex(chat => chat.NookId).IsUnique();
        builder.HasIndex(chat => new { chat.WorkspaceId, chat.Id });
        builder.HasIndex(chat => chat.HarnessTokenHash).IsUnique();
    }
}
