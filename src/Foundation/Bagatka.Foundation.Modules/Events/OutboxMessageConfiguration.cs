using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.Foundation.Modules.Events;

/// <summary>Maps <see cref="OutboxMessage"/> to the module's <c>outbox_messages</c> table.</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();
        builder.Property(message => message.Type).HasMaxLength(256);
        builder.Property(message => message.Payload).HasColumnType("jsonb");
        builder.HasIndex(message => message.RetryAt).HasFilter("parked_at IS NULL");
    }
}
