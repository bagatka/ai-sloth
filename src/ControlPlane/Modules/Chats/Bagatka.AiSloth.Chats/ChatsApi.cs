using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Harness;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

// The front door for both contracts. Each feature is a file in Features/. Streams that outlive a
// call use `databases`, never `db`.
internal sealed partial class ChatsApi(
    ChatsDbContext db,
    IDbContextFactory<ChatsDbContext> databases,
    IWorkspacesApi workspaces,
    INooksApi nooks,
    IAgentAccountsApi accounts,
    ChatRunners runners,
    ChatSignals signals,
    TimeProvider time) : IChatsApi, IChatHarnessesApi
{
    // The chat, if the actor may use it: every member of its workspace may.
    private async Task<Chat?> FindChatAsync(Actor actor, ChatId id, CancellationToken ct)
    {
        Chat? chat = await db.Chats.AsNoTracking().SingleOrDefaultAsync(found => found.Id == id, ct);
        if (chat is null)
        {
            return null;
        }

        WorkspaceRole? role = await workspaces.GetRoleAsync(actor, chat.WorkspaceId, ct);
        return role is null ? null : chat;
    }

    private async Task<ChatSummary> SummaryAsync(Chat chat, CancellationToken ct)
    {
        bool waiting = await db.Messages.AnyAsync(message => message.ChatId == chat.Id
            && (message.State == MessageState.New || message.State == MessageState.Queued || message.State == MessageState.Steering), ct);
        List<UserId> letIn = await db.Senders.Where(sender => sender.ChatId == chat.Id).OrderBy(sender => sender.UserId).Select(sender => sender.UserId).ToListAsync(ct);
        return chat.ToSummary(waiting, letIn);
    }
}
