using System;
using System.Threading;
using System.Threading.Tasks;
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
    ChatRunners runners,
    ChatSignals signals,
    TimeProvider time) : IChatsApi, IChatHarnessesApi
{
    // The chat, if the actor may use it: every member of its workspace may.
    private async Task<Chat?> FindChatAsync(Actor actor, ChatId id, CancellationToken ct)
    {
        Chat? chat = await db.Chats.AsNoTracking().SingleOrDefaultAsync(found => found.Id == id, ct);
        return chat is not null && await workspaces.GetRoleAsync(actor, chat.WorkspaceId, ct) is not null ? chat : null;
    }

    private async Task<bool> MessagesWaitingAsync(ChatId id, CancellationToken ct)
    {
        return await db.Messages.AnyAsync(message => message.ChatId == id
            && (message.State == MessageState.New || message.State == MessageState.Queued || message.State == MessageState.Steering), ct);
    }
}
