using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Workspaces;

internal sealed partial class WorkspacesApi
{
    public async Task<Result<Page<WorkspaceSummary>>> ListMineAsync(Actor actor, PageRequest page, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<Page<WorkspaceSummary>>(Error.Unauthorized);
        }

        if (!MembershipsOf(user.UserId).TakePage(membership => membership.Id, KeysetOrder.OldestFirst, page).TryGetValue(out IQueryable<Membership>? query, out Error? invalid))
        {
            return new Result<Page<WorkspaceSummary>>(invalid);
        }

        List<WorkspaceSummary> fetched = await query
            .Select(membership => new WorkspaceSummary(membership.Id, membership.Name.Value, membership.Role))
            .ToListAsync(ct);
        return new Result<Page<WorkspaceSummary>>(Keyset.ToPage(fetched, page, workspace => workspace.Id.Value));
    }
}
