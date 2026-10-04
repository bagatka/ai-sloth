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
        builder.Property(chat => chat.Harness).HasMaxLength(Chat.MaxHarnessLength);


        // Lists a nook's chats, and finds the chat a model call's token belongs to.
        builder.HasIndex(chat => new { chat.NookId, chat.Id });
        builder.HasIndex(chat => chat.HarnessTokenHash).IsUnique();
    }
}
