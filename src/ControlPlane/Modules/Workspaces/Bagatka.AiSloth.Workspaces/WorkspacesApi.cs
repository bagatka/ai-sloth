using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.AiSloth.Workspaces.Data;
using Bagatka.AiSloth.Workspaces.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Workspaces;

// The front door: dependencies and the access rule every feature shares. Each feature is a file in Features/.
internal sealed partial class WorkspacesApi(IDbContextFactory<WorkspacesDbContext> databases, TimeProvider time) : IWorkspacesApi
{
    // How deep resources nest: a nook in a project in a workspace, with room to spare.
    private const int MaxDepth = 4;

    // The highest level given to the user on the resource or on anything it is in. Workspaces are in
    // nothing, so a nook takes one query for its workspace and one for the grants.
    private static async Task<AccessLevel?> AccessOfAsync(WorkspacesDbContext db, UserId userId, Resource resource, CancellationToken ct)
    {
        List<Guid> reach = [resource.Id];
        List<Guid> unexplored = resource.Kind == ResourceKind.Workspace ? [] : [resource.Id];
        for (int depth = 0; depth < MaxDepth; depth++)
        {
            if (unexplored.Count == 0)
            {
                break;
            }

            List<Guid> current = unexplored;
            List<ResourceLink> links = await db.Links.AsNoTracking()
                .Where(link => current.Contains(link.ChildId))
                .ToListAsync(ct);
            reach.AddRange(links.Select(link => link.ParentId));
            unexplored = [.. links.Where(link => link.ParentKind != ResourceKind.Workspace).Select(link => link.ParentId)];
        }

        List<AccessLevel> levels = await db.Grants
            .Where(grant => grant.UserId == userId && reach.Contains(grant.ResourceId))
            .Select(grant => grant.Access)
            .ToListAsync(ct);
        return levels.Count == 0 ? null : levels.Max();
    }

    // Null when the actor has at least `needed` on the resource; otherwise why not: not found for no
    // access at all, so nobody learns that a resource exists, and forbidden for too little.
    private async Task<Error?> RefusalAsync(Actor actor, Resource resource, AccessLevel needed, CancellationToken ct)
    {
        AccessLevel? access = await GetAccessAsync(actor, resource, ct);
        if (access is null)
        {
            return WorkspacesErrors.ResourceNotFound;
        }

        if (access < needed)
        {
            return Error.Forbidden;
        }

        return null;
    }
}
