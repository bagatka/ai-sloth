using Bagatka.AiSloth.Chats.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Chats.Data;

internal sealed class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Text).HasMaxLength(Message.MaxTextLength);
        builder.Ignore(message => message.Waiting);

        // Finds the messages waiting for a chat's agent, oldest first.
        builder.HasIndex(message => new { message.ChatId, message.State, message.Id });
    }
}
