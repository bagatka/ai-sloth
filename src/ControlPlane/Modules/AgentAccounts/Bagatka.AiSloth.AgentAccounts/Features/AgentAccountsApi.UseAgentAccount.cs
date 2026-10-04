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
        AgentAccount? account = await FindInWorkspaceAsync(id, workspaceId, ct);
        if (account is null)
        {
            return new Result<AgentAccountCredential>(AgentAccountsErrors.NotFound);
        }

        bool allowed = await AllowedAsync(actor, account, workspaceId, ct);
        if (!allowed)
        {
            return new Result<AgentAccountCredential>(AgentAccountsErrors.NotFound);
        }

        return new Result<AgentAccountCredential>(account.ToCredential(box));
    }

    public async Task<bool?> MayUseAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        AgentAccount? account = await FindInWorkspaceAsync(id, workspaceId, ct);
        if (account is null)
        {
            return null;
        }

        return await AllowedAsync(actor, account, workspaceId, ct);
    }

    // The account, unless it doesn't exist or is another workspace's.
    private async Task<AgentAccount?> FindInWorkspaceAsync(AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        AgentAccount? account = await db.Accounts.AsNoTracking().SingleOrDefaultAsync(found => found.Id == id, ct);
        bool anotherWorkspaces = account?.WorkspaceId is not null && account.WorkspaceId != workspaceId;
        if (account is null || anotherWorkspaces)
        {
            return null;
        }

        return account;
    }

    // People with Write on the workspace use its accounts, people their own, and the control plane's
    // own processes any.
    private async Task<bool> AllowedAsync(Actor actor, AgentAccount account, WorkspaceId workspaceId, CancellationToken ct)
    {
        switch (actor)
        {
            case UserActor user when account.OwnerId is not null:
                return account.OwnerId == user.UserId;
            case UserActor:
                AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(workspaceId), ct);
                return access >= AccessLevel.Write;
            case SystemActor:
                return true;
            case AnonymousActor:
                return false;
        }

        throw new InvalidOperationException("The actor has no kind.");
    }
}
