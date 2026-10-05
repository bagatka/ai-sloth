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
        builder.Property(signIn => signIn.Callback).HasMaxLength(SignIn.MaxCallbackLength);
        builder.Property(signIn => signIn.State).HasMaxLength(SignIn.MaxValueLength);
        builder.Property(signIn => signIn.Nonce).HasMaxLength(SignIn.MaxValueLength);

        // A person's sign-ins, to clear the expired ones.
        builder.HasIndex(signIn => new { signIn.UserId, signIn.ExpiresAt });
    }
}
