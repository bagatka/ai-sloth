using Bagatka.AiSloth.Users.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Users.Data;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.HasKey(user => user.Id);
        builder.Property(user => user.Issuer).HasMaxLength(User.MaxIssuerLength);
        builder.Property(user => user.Subject).HasMaxLength(User.MaxSubjectLength);

        // One user per identity, which also makes a concurrent first sign-in lose cleanly.
        builder.HasIndex(user => new { user.Issuer, user.Subject }).IsUnique();
    }
}
