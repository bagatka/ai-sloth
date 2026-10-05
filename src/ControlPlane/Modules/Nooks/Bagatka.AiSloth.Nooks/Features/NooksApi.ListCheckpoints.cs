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
    public async Task<Result<Page<CheckpointSummary>>> ListCheckpointsAsync(Actor actor, NookId nookId, PageRequest page, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(actor, nookId, AccessLevel.Read, ct);
        if (nook.Failed)
        {
            return new Result<Page<CheckpointSummary>>(nook.Error);
        }

        Result<IQueryable<Checkpoint>> paged = db.Checkpoints.AsNoTracking()
            .Where(checkpoint => checkpoint.NookId == nookId)
            .TakePage(checkpoint => checkpoint.Id, KeysetOrder.NewestFirst, page);
        if (paged.Failed)
        {
            return new Result<Page<CheckpointSummary>>(paged.Error);
        }

        List<Checkpoint> fetched = await paged.Output.ToListAsync(ct);
        Page<Checkpoint> checkpoints = Keyset.ToPage(fetched, page, checkpoint => checkpoint.Id.Value);
        return new Result<Page<CheckpointSummary>>(new Page<CheckpointSummary>([.. checkpoints.Items.Select(checkpoint => checkpoint.ToSummary())], checkpoints.NextCursor));
    }
}
