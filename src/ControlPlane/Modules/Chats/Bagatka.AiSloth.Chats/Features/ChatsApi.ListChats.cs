using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<Page<ChatSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, PageRequest page, CancellationToken ct)
    {
        // A nook's guest doesn't see the workspace's other chats.
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(workspaceId), ct);
        if (access is null)
        {
            return new Result<Page<ChatSummary>>(WorkspacesErrors.NotFound);
        }

        Result<IQueryable<Chat>> paged = db.Chats.AsNoTracking()
            .Where(chat => chat.WorkspaceId == workspaceId && !db.Drafts.Any(draft => draft.ChatId == chat.Id))
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
        List<ChatSummary> fetched = [.. chats.Select(chat => chat.ToSummary(waiting.Contains(chat.Id)))];
        return new Result<Page<ChatSummary>>(Keyset.ToPage(fetched, page, chat => chat.Id.Value));
    }
}
