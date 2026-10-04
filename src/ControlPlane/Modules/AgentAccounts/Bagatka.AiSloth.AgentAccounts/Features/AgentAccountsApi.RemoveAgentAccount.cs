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
            WorkspaceRole? role = await workspaces.GetRoleAsync(actor, account.WorkspaceId.Value, ct);
            switch (role)
            {
                case null:
                    return new Result(AgentAccountsErrors.NotFound);
                case WorkspaceRole.Member:
                    return new Result(Error.Forbidden);
                case WorkspaceRole.Owner:
                    break;
            }
        }

        db.Accounts.Remove(account);
        return await db.SaveAsync(ct);
    }
}
