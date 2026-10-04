using Bagatka.AiSloth.AgentAccounts.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Bagatka.AiSloth.AgentAccounts.Data;

internal sealed class AgentAccountConfiguration : IEntityTypeConfiguration<AgentAccount>
{
    public void Configure(EntityTypeBuilder<AgentAccount> builder)
    {
        builder.ToTable("accounts", table => table.HasCheckConstraint("ck_accounts_one_owner", "(workspace_id IS NULL) <> (owner_id IS NULL)"));
        builder.HasKey(account => account.Id);

        // Lists a workspace's accounts and a person's own.
        builder.HasIndex(account => new { account.WorkspaceId, account.Id });
        builder.HasIndex(account => new { account.OwnerId, account.Id });
    }
}
