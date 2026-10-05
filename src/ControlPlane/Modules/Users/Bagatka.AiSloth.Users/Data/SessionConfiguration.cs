using Bagatka.AiSloth.Users.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Users.Data;

internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("sessions");
        builder.HasKey(session => session.Id);

        // Finds the session a call's token belongs to; lists a person's devices.
        builder.HasIndex(session => session.TokenHash).IsUnique();
        builder.HasIndex(session => session.UserId);
    }
}
