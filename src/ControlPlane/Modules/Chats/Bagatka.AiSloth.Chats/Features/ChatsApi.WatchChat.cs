using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    private const int WatchBatch = 500;

    public async Task<Result<IAsyncEnumerable<ChatEvent>>> WatchAsync(Actor actor, WatchChat command, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Chat> chat = await FindChatAsync(db, actor, command.ChatId, AccessLevel.Read, ct);
        if (chat.Failed)
        {
            return new Result<IAsyncEnumerable<ChatEvent>>(chat.Error);
        }

        return new Result<IAsyncEnumerable<ChatEvent>>(WatchEventsAsync(command.ChatId, command.AfterSequence, ct));
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
                bool more = await NextEventAsync(next, ct);
                if (!more)
                {
                    yield break;
                }
            }
        }
    }

    // Waits for the chat's next event; false when this instance hands over instead, which ends the
    // watch so its client resumes on the next instance.
    private async Task<bool> NextEventAsync(Task next, CancellationToken ct)
    {
        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(ct, active.Leaving);
        try
        {
            await next.WaitAsync(waiting.Token);
            return true;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }
}
