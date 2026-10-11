using Bagatka.AiSloth.Chats.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Chats.Data;

internal sealed class ModelCallConfiguration : IEntityTypeConfiguration<ModelCall>
{
    public void Configure(EntityTypeBuilder<ModelCall> builder)
    {
        builder.ToTable("model_calls");
        builder.HasKey(call => call.Id);
        builder.Property(call => call.Model).HasMaxLength(ModelCall.MaxModelLength);
        builder.Property(call => call.Cost).HasPrecision(18, 8);
        builder.Ignore(call => call.WholeInput);

        // A chat's calls, newest last; a workspace's, by day.
        builder.HasIndex(call => new { call.ChatId, call.At });
        builder.HasIndex(call => new { call.WorkspaceId, call.At });
    }
}
