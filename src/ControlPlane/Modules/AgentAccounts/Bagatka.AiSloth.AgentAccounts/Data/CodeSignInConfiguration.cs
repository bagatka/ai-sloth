using Bagatka.AiSloth.AgentAccounts.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.AgentAccounts.Data;

internal sealed class CodeSignInConfiguration : IEntityTypeConfiguration<CodeSignIn>
{
    public void Configure(EntityTypeBuilder<CodeSignIn> builder)
    {
        builder.ToTable("code_sign_ins");
        builder.HasKey(signIn => signIn.Id);

        // A person's sign-ins, to clear the expired ones.
        builder.HasIndex(signIn => new { signIn.UserId, signIn.ExpiresAt });
    }
}
