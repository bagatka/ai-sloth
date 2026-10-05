using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result> RemoveAsync(Actor actor, AgentAccountId id, CancellationToken ct)
    {
        AgentAccount? account = await db.Accounts.SingleOrDefaultAsync(found => found.Id == id, ct);
        if (account is null || actor is not UserActor user)
        {
            return new Result(AgentAccountsErrors.NotFound);
        }

        bool someoneElsesOwn = account.OwnerId is not null && account.OwnerId != user.UserId;
        if (someoneElsesOwn)
        {
            return new Result(AgentAccountsErrors.NotFound);
        }

        if (account.WorkspaceId is not null)
        {
            AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(account.WorkspaceId.Value), ct);
            if (access is null)
            {
                return new Result(AgentAccountsErrors.NotFound);
            }

            if (access < AccessLevel.Manage)
            {
                return new Result(Error.Forbidden);
            }
        }

        // Signing out ends the session at the vendor. When the vendor can't be reached, the account goes
        // anyway: its tokens are gone with it, and the person can disconnect the app at the vendor.
        PlanSession? session = account.Session(box);
        if (session is not null)
        {
            try
            {
                await chatGpt.RevokeAsync(session.ClientId, session.RefreshToken, ct);
            }
            catch (HttpRequestException exception)
            {
                Log.SignInNotEnded(logger, exception, account.Id.Value);
            }
        }

        db.Accounts.Remove(account);
        return await db.SaveAsync(ct);
    }
}
