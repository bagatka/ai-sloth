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
        if (await workspaces.GetRoleAsync(actor, workspaceId, ct) is null)
        {
            return new Result<Page<NookSummary>>(WorkspacesErrors.NotFound);
        }

        if (!db.Nooks.AsNoTracking()
                .Where(nook => nook.WorkspaceId == workspaceId)
                .TakePage(nook => nook.Id, KeysetOrder.NewestFirst, page)
                .TryGetValue(out IQueryable<Nook>? query, out Error? invalid))
        {
            return new Result<Page<NookSummary>>(invalid);
        }

        List<NookSummary> fetched = (await query.ToListAsync(ct)).Select(nook => nook.ToSummary()).ToList();
        return new Result<Page<NookSummary>>(Keyset.ToPage(fetched, page, nook => nook.Id.Value));
    }
}
