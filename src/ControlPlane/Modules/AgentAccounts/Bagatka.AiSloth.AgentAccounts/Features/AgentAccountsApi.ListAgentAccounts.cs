using System.Collections.Generic;
using System.Linq;
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
    public async Task<Result<IReadOnlyList<AgentAccountSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<IReadOnlyList<AgentAccountSummary>>(WorkspacesErrors.NotFound);
        }

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(workspaceId), ct);
        bool usesWorkspaceAccounts = access >= AccessLevel.Write;
        List<AgentAccount> accounts = await db.Accounts.AsNoTracking()
            .Where(account => (usesWorkspaceAccounts && account.WorkspaceId == workspaceId) || account.OwnerId == user.UserId)
            .ToListAsync(ct);
        return new Result<IReadOnlyList<AgentAccountSummary>>(accounts
            .OrderBy(account => account.OwnerId is null ? 0 : 1)
            .ThenBy(account => account.Id.Value)
            .Select(account => account.ToSummary())
            .ToList());
    }
}
