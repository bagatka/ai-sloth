using Bagatka.AiSloth.Workspaces.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.Workspaces.Data;

internal sealed class MemberConfiguration : IEntityTypeConfiguration<Member>
{
    public void Configure(EntityTypeBuilder<Member> builder)
    {
        // One membership per workspace and user.
        builder.HasKey(member => new { member.WorkspaceId, member.UserId });

        // Lists a user's workspaces.
        builder.HasIndex(member => new { member.UserId, member.WorkspaceId });
    }
}
