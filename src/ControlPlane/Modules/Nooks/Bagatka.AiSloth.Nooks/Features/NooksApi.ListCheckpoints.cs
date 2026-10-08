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
    public async Task<Result<Page<CheckpointSummary>>> ListCheckpointsAsync(Actor actor, NookId nookId, PageRequest page, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Nook> nook = await FindNookAsync(db, actor, nookId, AccessLevel.Read, ct);
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
        Page<Checkpoint> listed = Keyset.ToPage(fetched, page, checkpoint => checkpoint.Id.Value);
        return new Result<Page<CheckpointSummary>>(new Page<CheckpointSummary>([.. listed.Items.Select(checkpoint => checkpoint.ToSummary())], listed.NextCursor));
    }
}
