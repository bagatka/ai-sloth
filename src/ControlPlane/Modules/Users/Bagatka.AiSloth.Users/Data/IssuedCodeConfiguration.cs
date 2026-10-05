using Bagatka.AiSloth.Users.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Users.Data;

internal sealed class IssuedCodeConfiguration : IEntityTypeConfiguration<IssuedCode>
{
    public void Configure(EntityTypeBuilder<IssuedCode> builder)
    {
        builder.ToTable("issued_codes");
        builder.HasKey(code => code.Id);
        builder.HasIndex(code => code.CodeHash).IsUnique();

        // Clears a person's expired link codes.
        builder.HasIndex(code => new { code.UserId, code.ExpiresAt });
    }
}
