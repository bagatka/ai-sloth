using Bagatka.AiSloth.Nooks.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation.Modules;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<Page<ProcessSummary>>> ListProcessesAsync(Actor actor, NookId nookId, PageRequest page, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Nook> nook = await FindNookAsync(db, actor, nookId, AccessLevel.Read, ct);
        if (nook.Failed)
        {
            return new Result<Page<ProcessSummary>>(nook.Error);
        }

        Result<IQueryable<Process>> paged = db.Processes.AsNoTracking()
            .Where(process => process.NookId == nookId)
            .TakePage(process => process.Id, KeysetOrder.NewestFirst, page);
        if (paged.Failed)
        {
            return new Result<Page<ProcessSummary>>(paged.Error);
        }

        List<Process> listed = await paged.Output.ToListAsync(ct);
        List<ProcessSummary> fetched = listed.Select(process => process.ToSummary()).ToList();
        return new Result<Page<ProcessSummary>>(Keyset.ToPage(fetched, page, process => process.Id.Value));
    }
}
