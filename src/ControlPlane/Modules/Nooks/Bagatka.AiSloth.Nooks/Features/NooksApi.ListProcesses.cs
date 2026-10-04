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
        if (await FindNookAsync(actor, nookId, ct) is null)
        {
            return new Result<Page<ProcessSummary>>(NooksErrors.NotFound);
        }

        if (!db.Processes.AsNoTracking()
                .Where(process => process.NookId == nookId)
                .TakePage(process => process.Id, KeysetOrder.NewestFirst, page)
                .TryGetValue(out IQueryable<Process>? query, out Error? invalid))
        {
            return new Result<Page<ProcessSummary>>(invalid);
        }

        List<ProcessSummary> fetched = (await query.ToListAsync(ct)).Select(process => process.ToSummary()).ToList();
        return new Result<Page<ProcessSummary>>(Keyset.ToPage(fetched, page, process => process.Id.Value));
    }
}
