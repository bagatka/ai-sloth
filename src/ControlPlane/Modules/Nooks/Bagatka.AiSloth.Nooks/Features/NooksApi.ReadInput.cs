using Bagatka.AiSloth.Nooks.Data;
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
    private static readonly Error InputNotFound = Error.NotFound("nooks.input_not_found", "No input waits for this process.");

    public async Task<Result<IAsyncEnumerable<ReadOnlyMemory<byte>>>> ReadInputAsync(Actor actor, ReadInput command, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Nook? nook = await db.Nooks.AsNoTracking().SingleOrDefaultAsync(found => found.Id == command.NookId, ct);
        if (nook is null || !nook.AcceptsDaemonToken(command.Token))
        {
            return new Result<IAsyncEnumerable<ReadOnlyMemory<byte>>>(Error.Unauthorized);
        }

        InputFeed? feed = feeds.Take(command.NookId, command.ProcessId);
        return feed is null
            ? new Result<IAsyncEnumerable<ReadOnlyMemory<byte>>>(InputNotFound)
            : new Result<IAsyncEnumerable<ReadOnlyMemory<byte>>>(feed.ReadAsync(ct));
    }
}
