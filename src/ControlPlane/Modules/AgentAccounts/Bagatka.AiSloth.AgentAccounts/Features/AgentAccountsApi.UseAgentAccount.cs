using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

        if (account.NeedsSignIn)
        {
            return new Result<AgentAccountCredential>(AgentAccountsErrors.SignInEnded);
        }

        bool renewalDue = account.RenewalDue(box, time.GetUtcNow());
        if (renewalDue)
        {
            Result<AgentAccount> renewed = await RenewAsync(id);
            if (renewed.Failed)
            {
                return new Result<AgentAccountCredential>(renewed.Error);
            }

            account = renewed.Output;
        }

        return new Result<AgentAccountCredential>(account.ToCredential(box, settings.ChatGptApi));
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

    // Renews a plan's access token, one renewal at a time per account: OpenAI replaces the refresh
    // token with every renewal and refuses one used twice, so the account's row stays locked while
    // OpenAI answers (within the client's timeout), and whoever waited finds it renewed. The caller's
    // cancellation doesn't apply: a renewal OpenAI completed but this side dropped would end the sign-in.
    private async Task<Result<AgentAccount>> RenewAsync(AgentAccountId id)
    {
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        AgentAccount? account = await db.Accounts
            .FromSql($"SELECT * FROM agent_accounts.accounts WHERE id = {id.Value} FOR UPDATE")
            .SingleOrDefaultAsync(CancellationToken.None);
        if (account is null)
        {
            return new Result<AgentAccount>(AgentAccountsErrors.NotFound);
        }

        PlanSession? session = account.Session(box);
        if (session is null)
        {
            return new Result<AgentAccount>(AgentAccountsErrors.SignInEnded);
        }

        bool renewedMeanwhile = !account.RenewalDue(box, time.GetUtcNow());
        if (renewedMeanwhile)
        {
            return new Result<AgentAccount>(account);
        }

        Result<ChatGptTokens> tokens = await chatGpt.RefreshAsync(session.ClientId, session.RefreshToken, CancellationToken.None);
        if (tokens.Failed)
        {
            // Every refusal means the session is over, such as after the person disconnected the app.
            account.SignInEnded(time);
            Log.SignInEnded(logger, account.Id.Value, tokens.Error.Code);
        }
        else
        {
            account.Renewed(session.Renewed(tokens.Output, time.GetUtcNow()), box);
        }

        Result saved = await db.SaveAsync(CancellationToken.None);
        if (saved.Failed)
        {
            return new Result<AgentAccount>(saved.Error);
        }

        await transaction.CommitAsync(CancellationToken.None);
        if (tokens.Failed)
        {
            return new Result<AgentAccount>(AgentAccountsErrors.SignInEnded);
        }

        return new Result<AgentAccount>(account);
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
