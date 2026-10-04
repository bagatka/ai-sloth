using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<Page<ProcessSummary>>> ListProcessesAsync(Actor actor, NookId nookId, PageRequest page, CancellationToken ct)
    {
        Nook? nook = await FindNookAsync(actor, nookId, ct);
        if (nook is null)
        {
            return new Result<Page<ProcessSummary>>(NooksErrors.NotFound);
        }

        Result<IQueryable<Process>> paged = db.Processes.AsNoTracking()
            .Where(process => process.NookId == nookId)
            .TakePage(process => process.Id, KeysetOrder.NewestFirst, page);
        if (paged.Failed)
        {
            return new Result<Page<ProcessSummary>>(paged.Error);
        }

        List<Process> processes = await paged.Output.ToListAsync(ct);
        List<ProcessSummary> fetched = processes.Select(process => process.ToSummary()).ToList();
        return new Result<Page<ProcessSummary>>(Keyset.ToPage(fetched, page, process => process.Id.Value));
    }
}
