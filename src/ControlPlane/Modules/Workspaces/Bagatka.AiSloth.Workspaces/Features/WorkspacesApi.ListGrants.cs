using Bagatka.AiSloth.Workspaces.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.AiSloth.Workspaces.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Workspaces;

internal sealed partial class WorkspacesApi
{
    public async Task<Result<IReadOnlyList<GrantSummary>>> ListGrantsAsync(Actor actor, Resource resource, CancellationToken ct)
    {
        await using WorkspacesDbContext db = await databases.CreateDbContextAsync(ct);

        ArgumentNullException.ThrowIfNull(resource);
        Error? refused = await RefusalAsync(actor, resource, AccessLevel.Read, ct);
        if (refused is not null)
        {
            return new Result<IReadOnlyList<GrantSummary>>(refused);
        }

        // Not handled: a resource shared with thousands of people; paging it takes a Page<T> here.
        List<Grant> grants = await db.Grants.AsNoTracking()
            .Where(grant => grant.ResourceId == resource.Id)
            .OrderBy(grant => grant.UserId)
            .ToListAsync(ct);
        return new Result<IReadOnlyList<GrantSummary>>([.. grants.Select(grant => grant.ToSummary())]);
    }
}
