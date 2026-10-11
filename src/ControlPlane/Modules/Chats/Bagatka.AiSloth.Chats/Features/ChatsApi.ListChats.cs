using Bagatka.AiSloth.Chats.Data;
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
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        // A nook's guest doesn't see the workspace's other chats.
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(workspaceId), ct);
        if (access is null)
        {
            return new Result<Page<ChatSummary>>(WorkspacesErrors.NotFound);
        }

        Result<IQueryable<Chat>> paged = Listed(db, workspaceId).TakePage(chat => chat.Id, KeysetOrder.NewestFirst, page);
        if (paged.Failed)
        {
            return new Result<Page<ChatSummary>>(paged.Error);
        }

        List<Chat> chats = await paged.Output.ToListAsync(ct);
        List<ChatSummary> fetched = await SummariesAsync(db, chats, ct);
        return new Result<Page<ChatSummary>>(Keyset.ToPage(fetched, page, chat => chat.Id.Value));
    }

    // The workspace's chats lists show: those someone wrote in, so no draft.
    private static IQueryable<Chat> Listed(ChatsDbContext db, WorkspaceId workspaceId)
    {
        return db.Chats.AsNoTracking().Where(chat => chat.WorkspaceId == workspaceId && !db.Drafts.Any(draft => draft.ChatId == chat.Id));
    }

    // The chats as lists show them, in their order: whether messages wait for each, its first, and what
    // it has cost.
    private static async Task<List<ChatSummary>> SummariesAsync(ChatsDbContext db, List<Chat> chats, CancellationToken ct)
    {
        List<ChatId> ids = [.. chats.Select(chat => chat.Id)];
        List<ChatId> waitingChats = await db.Messages
            .Where(message => ids.Contains(message.ChatId)
                && (message.State == MessageState.New || message.State == MessageState.Queued || message.State == MessageState.Steering))
            .Select(message => message.ChatId)
            .Distinct()
            .ToListAsync(ct);
        HashSet<ChatId> waiting = [.. waitingChats];
        Dictionary<ChatId, string> firstMessages = await db.Messages
            .Where(message => ids.Contains(message.ChatId))
            .GroupBy(message => message.ChatId)
            .Select(chatMessages => new
            {
                ChatId = chatMessages.Key,
                Text = chatMessages.OrderBy(message => message.SentAt).ThenBy(message => message.Id).Select(message => message.Text.Substring(0, FirstMessageStart)).First(),
            })
            .ToDictionaryAsync(first => first.ChatId, first => first.Text, ct);
        Dictionary<ChatId, Spent<ChatId>> spent = await db.ModelCalls
            .Where(call => ids.Contains(call.ChatId))
            .GroupBy(call => call.ChatId)
            .Select(SpentOf<ChatId>())
            .ToDictionaryAsync(chatSpent => chatSpent.Key, ct);
        return [.. chats.Select(chat => chat.ToSummary(
            waiting.Contains(chat.Id),
            firstMessages.GetValueOrDefault(chat.Id),
            spent.GetValueOrDefault(chat.Id)?.Cost(),
            spent.GetValueOrDefault(chat.Id)?.Estimate == true))];
    }
}
