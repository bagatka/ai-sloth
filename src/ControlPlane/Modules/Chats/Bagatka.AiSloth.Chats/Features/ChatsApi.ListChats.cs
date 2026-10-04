using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<Page<ChatSummary>>> ListAsync(Actor actor, NookId nookId, PageRequest page, CancellationToken ct)
    {
        if (!(await nooks.GetAsync(actor, nookId, ct)).TryGetValue(out _, out Error? missing))
        {
            return new Result<Page<ChatSummary>>(missing);
        }

        if (!db.Chats.AsNoTracking()
                .Where(chat => chat.NookId == nookId)
                .TakePage(chat => chat.Id, KeysetOrder.NewestFirst, page)
                .TryGetValue(out IQueryable<Chat>? query, out Error? invalid))
        {
            return new Result<Page<ChatSummary>>(invalid);
        }

        List<Chat> chats = await query.ToListAsync(ct);
        List<ChatId> ids = [.. chats.Select(chat => chat.Id)];
        HashSet<ChatId> waiting = [.. await db.Messages
            .Where(message => ids.Contains(message.ChatId)
                && (message.State == MessageState.New || message.State == MessageState.Queued || message.State == MessageState.Steering))
            .Select(message => message.ChatId)
            .Distinct()
            .ToListAsync(ct)];
        List<ChatSummary> fetched = [.. chats.Select(chat => chat.ToSummary(waiting.Contains(chat.Id)))];
        return new Result<Page<ChatSummary>>(Keyset.ToPage(fetched, page, chat => chat.Id.Value));
    }
}
