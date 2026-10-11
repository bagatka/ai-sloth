using Bagatka.AiSloth.AgentAccounts.Data;
using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Kinds;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result<AgentAccountCredential>> UseAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        Result<AgentAccount> account = await UsableAsync(actor, id, workspaceId, ct);
        if (account.Failed)
        {
            return new Result<AgentAccountCredential>(account.Error);
        }

        return new Result<AgentAccountCredential>(CredentialOf(account.Output));
    }

    // What running an agent on the account takes: where its calls go, and the headers that pay for them.
    private AgentAccountCredential CredentialOf(AgentAccount account)
    {
        ModelEndpoint endpoint = kinds.Of(account.Kind).Endpoint(account.OpenCredential(box), account.Endpoint?.Value);
        return new AgentAccountCredential(account.Id, account.Kind, account.OwnerId, endpoint);
    }

    // The account, if the actor may use it in the workspace, with its credential renewed when due.
    private async Task<Result<AgentAccount>> UsableAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        await using AgentAccountsDbContext db = await databases.CreateDbContextAsync(ct);

        AgentAccount? account = await FindInWorkspaceAsync(db, id, workspaceId, ct);
        if (account is null)
        {
            return new Result<AgentAccount>(AgentAccountsErrors.NotFound);
        }

        bool allowed = await AllowedAsync(actor, account, workspaceId, ct);
        if (!allowed)
        {
            return new Result<AgentAccount>(AgentAccountsErrors.NotFound);
        }

        // Not handled: signing in again while keeping the account; its owner removes it and adds it again.
        if (account.NeedsSignIn)
        {
            return new Result<AgentAccount>(AgentAccountsErrors.SignInEnded);
        }

        bool renewalDue = account.RenewalDue(box, time.GetUtcNow());
        if (renewalDue)
        {
            return await RenewAsync(db, id);
        }

        return new Result<AgentAccount>(account);
    }

    public async Task<bool?> MayUseAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        await using AgentAccountsDbContext db = await databases.CreateDbContextAsync(ct);

        AgentAccount? account = await FindInWorkspaceAsync(db, id, workspaceId, ct);
        if (account is null)
        {
            return null;
        }

        return await AllowedAsync(actor, account, workspaceId, ct);
    }

    // Renews a plan's credential, one renewal at a time per account: the vendor replaces the refresh
    // token with every renewal and may refuse one used twice, so the account's row stays locked while
    // the vendor answers (within the client's timeout), and whoever waited finds it renewed. The
    // caller's cancellation doesn't apply: a renewal the vendor completed but this side dropped would
    // end the sign-in.
    private async Task<Result<AgentAccount>> RenewAsync(AgentAccountsDbContext db, AgentAccountId id)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        AgentAccount? account = await db.Accounts
            .FromSql($"SELECT * FROM agent_accounts.accounts WHERE id = {id.Value} FOR UPDATE")
            .SingleOrDefaultAsync(CancellationToken.None);
        if (account is null)
        {
            return new Result<AgentAccount>(AgentAccountsErrors.NotFound);
        }

        if (account.NeedsSignIn)
        {
            return new Result<AgentAccount>(AgentAccountsErrors.SignInEnded);
        }

        bool renewedMeanwhile = !account.RenewalDue(box, time.GetUtcNow());
        if (renewedMeanwhile)
        {
            return new Result<AgentAccount>(account);
        }

        IPlanKind plan = (IPlanKind)kinds.Of(account.Kind);
        Result<Credential> renewed = await plan.RenewAsync(account.OpenCredential(box), CancellationToken.None);
        if (renewed.Failed)
        {
            // Every refusal means the sign-in is over, such as after the person signed out everywhere.
            account.SignInEnded(time);
            Log.SignInEnded(logger, account.Id.Value, renewed.Error.Code);
        }
        else
        {
            account.Renewed(renewed.Output, box);
        }

        Result saved = await db.SaveAsync(CancellationToken.None);
        if (saved.Failed)
        {
            return new Result<AgentAccount>(saved.Error);
        }

        await transaction.CommitAsync(CancellationToken.None);
        if (renewed.Failed)
        {
            return new Result<AgentAccount>(AgentAccountsErrors.SignInEnded);
        }

        return new Result<AgentAccount>(account);
    }

    // The account, unless it doesn't exist or is another workspace's.
    private static async Task<AgentAccount?> FindInWorkspaceAsync(AgentAccountsDbContext db, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
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
