using Bagatka.AiSloth.AgentAccounts.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.AgentAccounts.Data;

internal sealed class SignInConfiguration : IEntityTypeConfiguration<SignIn>
{
    public void Configure(EntityTypeBuilder<SignIn> builder)
    {
        builder.ToTable("sign_ins");
        builder.HasKey(signIn => signIn.Id);
        builder.Property(signIn => signIn.UserCode).HasMaxLength(SignIn.MaxUserCodeLength);

        // A person's sign-ins, to clear the expired ones.
        builder.HasIndex(signIn => new { signIn.UserId, signIn.ExpiresAt });
    }
}
