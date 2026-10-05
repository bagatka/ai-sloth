using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<NookSummary>> GetAsync(Actor actor, NookId id, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(actor, id, AccessLevel.Read, ct);
        if (nook.Failed)
        {
            return new Result<NookSummary>(nook.Error);
        }

        List<SourceCopy> copies = await db.SourceCopies.AsNoTracking().Where(copy => copy.NookId == id).OrderBy(copy => copy.Name).ToListAsync(ct);
        return new Result<NookSummary>(nook.Output.ToSummary([.. copies.Select(copy => copy.ToContract())]));
    }
}
