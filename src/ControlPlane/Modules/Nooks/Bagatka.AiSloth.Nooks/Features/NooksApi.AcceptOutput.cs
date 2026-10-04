using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    private static readonly Error WatchEnded = Error.NotFound("nooks.watch_ended", "Nobody watches this upload anymore.");

    public async Task<Result> AcceptOutputAsync(Actor actor, OutputUpload upload, IAsyncEnumerable<ProcessOutput> output, CancellationToken ct)
    {
        Nook? nook = await db.Nooks.AsNoTracking().SingleOrDefaultAsync(found => found.Id == upload.NookId, ct);
        if (nook is null || !nook.AcceptsDaemonToken(upload.Token))
        {
            return new Result(Error.Unauthorized);
        }

        OutputReceiver? receiver = daemons.Find(upload.WatchId);
        if (receiver is null || receiver.Connection.NookId != upload.NookId)
        {
            return new Result(WatchEnded);
        }

        // The watcher leaving cancels the upload, which tells the daemon to stop.
        using CancellationTokenSource watched = CancellationTokenSource.CreateLinkedTokenSource(ct, receiver.WatcherLeft);
        bool complete = false;
        try
        {
            await foreach (ProcessOutput chunk in output.WithCancellation(watched.Token))
            {
                if (!await receiver.DeliverAsync(chunk, watched.Token))
                {
                    return new Result(new Success());
                }
            }

            complete = true;
            return new Result(new Success());
        }
        catch (OperationCanceledException) when (receiver.WatcherLeft.IsCancellationRequested)
        {
            return new Result(new Success());
        }
        finally
        {
            receiver.End(complete);
        }
    }
}
