using Bagatka.AiSloth.Nooks.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<Page<NookSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, PageRequest page, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        // A nook's guest doesn't see the workspace's other nooks.
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(workspaceId), ct);
        if (access is null)
        {
            return new Result<Page<NookSummary>>(WorkspacesErrors.NotFound);
        }

        Result<IQueryable<Nook>> paged = db.Nooks.AsNoTracking()
            .Where(nook => nook.WorkspaceId == workspaceId)
            .TakePage(nook => nook.Id, KeysetOrder.NewestFirst, page);
        if (paged.Failed)
        {
            return new Result<Page<NookSummary>>(paged.Error);
        }

        List<Nook> nooks = await paged.Output.ToListAsync(ct);
        List<NookId> ids = [.. nooks.Select(nook => nook.Id)];
        List<SourceCopy> copies = await db.SourceCopies.AsNoTracking().Where(copy => ids.Contains(copy.NookId)).OrderBy(copy => copy.Name).ToListAsync(ct);
        ILookup<NookId, NookSource> byNook = copies.ToLookup(copy => copy.NookId, copy => copy.ToContract());
        List<NookSummary> fetched = nooks.Select(nook => SummaryOf(nook, [.. byNook[nook.Id]])).ToList();
        return new Result<Page<NookSummary>>(Keyset.ToPage(fetched, page, nook => nook.Id.Value));
    }
}
