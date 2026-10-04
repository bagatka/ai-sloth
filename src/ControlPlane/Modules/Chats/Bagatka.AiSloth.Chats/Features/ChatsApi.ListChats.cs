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
        Result<NookSummary> nook = await nooks.GetAsync(actor, nookId, ct);
        if (nook.Failed)
        {
            return new Result<Page<ChatSummary>>(nook.Error);
        }

        Result<IQueryable<Chat>> paged = db.Chats.AsNoTracking()
            .Where(chat => chat.NookId == nookId)
            .TakePage(chat => chat.Id, KeysetOrder.NewestFirst, page);
        if (paged.Failed)
        {
            return new Result<Page<ChatSummary>>(paged.Error);
        }

        List<Chat> chats = await paged.Output.ToListAsync(ct);
        List<ChatId> ids = [.. chats.Select(chat => chat.Id)];
        List<ChatId> waitingChats = await db.Messages
            .Where(message => ids.Contains(message.ChatId)
                && (message.State == MessageState.New || message.State == MessageState.Queued || message.State == MessageState.Steering))
            .Select(message => message.ChatId)
            .Distinct()
            .ToListAsync(ct);
        HashSet<ChatId> waiting = [.. waitingChats];
        List<ChatSender> senders = await db.Senders.Where(sender => ids.Contains(sender.ChatId)).OrderBy(sender => sender.UserId).ToListAsync(ct);
        ILookup<ChatId, UserId> letIn = senders.ToLookup(sender => sender.ChatId, sender => sender.UserId);
        List<ChatSummary> fetched = [.. chats.Select(chat => chat.ToSummary(waiting.Contains(chat.Id), letIn[chat.Id]))];
        return new Result<Page<ChatSummary>>(Keyset.ToPage(fetched, page, chat => chat.Id.Value));
    }
}
