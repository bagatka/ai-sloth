using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    // How many of a workspace's newest chats a list watch follows.
    private const int WatchedChats = 100;

    public async Task<Result<IAsyncEnumerable<IReadOnlyList<ChatSummary>>>> WatchChatsAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct)
    {
        // A nook's guest doesn't see the workspace's other chats.
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(workspaceId), ct);
        if (access is null)
        {
            return new Result<IAsyncEnumerable<IReadOnlyList<ChatSummary>>>(WorkspacesErrors.NotFound);
        }

        return new Result<IAsyncEnumerable<IReadOnlyList<ChatSummary>>>(WatchListedAsync(actor, workspaceId, ct));
    }

    // Reads the newest chats again at each change, and gives the ones that differ from what it gave
    // before, so a watcher holds at most WatchedChats summaries and a change costs one list query.
    private async IAsyncEnumerable<IReadOnlyList<ChatSummary>> WatchListedAsync(Actor actor, WorkspaceId workspaceId, [EnumeratorCancellation] CancellationToken ct)
    {
        Dictionary<ChatId, ChatSummary> given = [];
        bool first = true;
        while (true)
        {
            Task next = signals.NextAsync(workspaceId);
            AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(workspaceId), ct);
            if (access is null)
            {
                yield break;
            }

            List<ChatSummary> newest;
            await using (ChatsDbContext reading = await databases.CreateDbContextAsync(ct))
            {
                List<Chat> chats = await Listed(reading, workspaceId).OrderByDescending(chat => chat.Id).Take(WatchedChats).ToListAsync(ct);
                newest = await SummariesAsync(reading, chats, ct);
            }

            List<ChatSummary> changed = [.. newest.Where(summary => given.GetValueOrDefault(summary.Id) != summary)];
            given = newest.ToDictionary(summary => summary.Id);
            if (first || changed.Count > 0)
            {
                first = false;
                yield return changed;
            }

            bool more = await NextEventAsync(next, ct);
            if (!more)
            {
                yield break;
            }
        }
    }
}
