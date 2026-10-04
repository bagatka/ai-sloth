using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result<AgentAccountCredential>> UseAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        AgentAccount? account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(found => found.Id == id, ct);
        bool anotherWorkspaces = account?.WorkspaceId is not null && account.WorkspaceId != workspaceId;
        if (account is null || anotherWorkspaces)
        {
            return new Result<AgentAccountCredential>(AgentAccountsErrors.NotFound);
        }

        bool allowed = await MayUseAsync(actor, account, workspaceId, ct);
        if (!allowed)
        {
            return new Result<AgentAccountCredential>(AgentAccountsErrors.NotFound);
        }

        return new Result<AgentAccountCredential>(account.ToCredential(box, settings.SharePersonalAccounts));
    }

    // Members use their workspace's accounts and people their own; the control plane's own processes any.
    private async Task<bool> MayUseAsync(Actor actor, AgentAccount account, WorkspaceId workspaceId, CancellationToken ct)
    {
        switch (actor)
        {
            case UserActor user when account.OwnerId is not null:
                return account.OwnerId == user.UserId;
            case UserActor:
                WorkspaceRole? role = await workspaces.GetRoleAsync(actor, workspaceId, ct);
                return role is not null;
            case SystemActor:
                return true;
            case AnonymousActor:
                return false;
        }

        throw new InvalidOperationException("The actor has no kind.");
    }
}
