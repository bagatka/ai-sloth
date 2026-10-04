using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.AiSloth.Workspaces.Model;
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

        // A person is in a handful of workspaces, so their grants come first and the page after.
        List<Grant> grants = await db.Grants.AsNoTracking()
            .Where(grant => grant.UserId == user.UserId && grant.ResourceKind == ResourceKind.Workspace)
            .ToListAsync(ct);
        Dictionary<Guid, AccessLevel> access = grants.ToDictionary(grant => grant.ResourceId, grant => grant.Access);
        List<WorkspaceId> ids = [.. grants.Select(grant => WorkspaceId.From(grant.ResourceId))];
        Result<IQueryable<Workspace>> paged = db.Workspaces.AsNoTracking()
            .Where(workspace => ids.Contains(workspace.Id))
            .TakePage(workspace => workspace.Id, KeysetOrder.OldestFirst, page);
        if (paged.Failed)
        {
            return new Result<Page<WorkspaceSummary>>(paged.Error);
        }

        List<Workspace> workspaces = await paged.Output.ToListAsync(ct);
        List<WorkspaceSummary> fetched = [.. workspaces.Select(workspace => new WorkspaceSummary(workspace.Id, workspace.Name.Value, access[workspace.Id.Value]))];
        return new Result<Page<WorkspaceSummary>>(Keyset.ToPage(fetched, page, workspace => workspace.Id.Value));
    }
}
