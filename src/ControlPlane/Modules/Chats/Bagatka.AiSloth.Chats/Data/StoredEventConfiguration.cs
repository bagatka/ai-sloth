using Bagatka.AiSloth.Chats.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Chats.Data;

internal sealed class StoredEventConfiguration : IEntityTypeConfiguration<StoredEvent>
{
    public void Configure(EntityTypeBuilder<StoredEvent> builder)
    {
        builder.ToTable("events");

        // A chat's events are read in sequence order, after a sequence number.
        builder.HasKey(stored => new { stored.ChatId, stored.Sequence });
        builder.Property(stored => stored.Kind).HasMaxLength(StoredEvent.MaxKindLength);
        builder.Property(stored => stored.Data).HasColumnType("jsonb");
    }
}
