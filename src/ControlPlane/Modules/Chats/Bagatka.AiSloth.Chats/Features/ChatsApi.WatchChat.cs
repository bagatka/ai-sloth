using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    private const int WatchBatch = 500;

    public async Task<Result<IAsyncEnumerable<ChatEvent>>> WatchAsync(Actor actor, WatchChat command, CancellationToken ct)
    {
        return await FindChatAsync(actor, command.ChatId, ct) is null
            ? new Result<IAsyncEnumerable<ChatEvent>>(ChatsErrors.NotFound)
            : new Result<IAsyncEnumerable<ChatEvent>>(WatchEventsAsync(command.ChatId, command.AfterSequence, ct));
    }

    private async IAsyncEnumerable<ChatEvent> WatchEventsAsync(ChatId id, long after, [EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            Task next = signals.NextAsync(id);
            List<StoredEvent> batch;
            await using (ChatsDbContext reading = await databases.CreateDbContextAsync(ct))
            {
                batch = await reading.Events.AsNoTracking()
                    .Where(stored => stored.ChatId == id && stored.Sequence > after)
                    .OrderBy(stored => stored.Sequence)
                    .Take(WatchBatch)
                    .ToListAsync(ct);
            }

            foreach (StoredEvent stored in batch)
            {
                after = stored.Sequence;
                yield return stored.ToContract();
            }

            if (batch.Count == 0)
            {
                await next.WaitAsync(ct);
            }
        }
    }
}
