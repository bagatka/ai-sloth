using Bagatka.AiSloth.Nooks.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<IReadOnlyList<SourceChanges>>> GetChangesAsync(Actor actor, NookId nookId, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Nook> nook = await FindNookAsync(db, actor, nookId, AccessLevel.Read, ct);
        if (nook.Failed)
        {
            return new Result<IReadOnlyList<SourceChanges>>(nook.Error);
        }

        // An awake nook says them as they are now; reading them isn't using it.
        DaemonConnection? awake = await daemons.WaitAsync(nookId, TimeSpan.Zero, ct);
        if (awake is not null)
        {
            Result<List<SourceChanges>> live = await diffs.ReadAsync(db, nookId, awake, ct);
            return live.Failed ? new Result<IReadOnlyList<SourceChanges>>(live.Error) : new Result<IReadOnlyList<SourceChanges>>(live.Output);
        }

        // Otherwise its latest checkpoint kept them; a nook takes one before it sleeps. A nook without
        // any has only its sources as they were copied in.
        Checkpoint? latest = await db.Checkpoints.AsNoTracking().Where(found => found.NookId == nookId).OrderByDescending(found => found.Number).FirstOrDefaultAsync(ct);
        if (latest is null)
        {
            return new Result<IReadOnlyList<SourceChanges>>([]);
        }

        if (latest.ChangedFiles is not null)
        {
            List<SourceChanges>? kept = await checkpoints.KeptChangesAsync(nookId, latest.Number, ct);
            if (kept is not null)
            {
                return new Result<IReadOnlyList<SourceChanges>>(kept);
            }
        }

        // Not handled: a nook asleep since before checkpoints kept their changes wakes to read them
        // once; its next checkpoint keeps them.
        Result<DaemonConnection> ready = await ReadyAsync(db, actor, nook.Output, changes: false, ct);
        if (ready.Failed)
        {
            return new Result<IReadOnlyList<SourceChanges>>(ready.Error);
        }

        Result<List<SourceChanges>> read = await diffs.ReadAsync(db, nookId, ready.Output, ct);
        return read.Failed ? new Result<IReadOnlyList<SourceChanges>>(read.Error) : new Result<IReadOnlyList<SourceChanges>>(read.Output);
    }
}
