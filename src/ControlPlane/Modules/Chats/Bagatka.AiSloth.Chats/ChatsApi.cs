using System;
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
using Bagatka.ObjectStorage;
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
    IObjectStorage storage,
    ChatsSettings settings,
    TimeProvider time) : IChatsApi, IChatHarnessesApi
{
    // The chat, if the actor may do at least `needed` with it: a chat is as open as its nook. Not found
    // when they may not see it, so nobody learns that it exists; forbidden when they may only read.
    private async Task<Result<Chat>> FindChatAsync(Actor actor, ChatId id, AccessLevel needed, CancellationToken ct)
    {
        Chat? chat = await db.Chats.AsNoTracking().SingleOrDefaultAsync(found => found.Id == id, ct);
        if (chat is null)
        {
            return new Result<Chat>(ChatsErrors.NotFound);
        }

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Nook(chat.NookId.Value), ct);
        if (access is null)
        {
            return new Result<Chat>(ChatsErrors.NotFound);
        }

        if (access < needed)
        {
            return new Result<Chat>(Error.Forbidden);
        }

        return new Result<Chat>(chat);
    }

    // The person acting, if they may do at least `needed` with the workspace: their own settings
    // there, such as harness state, need a person with access. Not found without access.
    private async Task<Result<UserId>> PersonInAsync(Actor actor, WorkspaceId workspaceId, AccessLevel needed, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<UserId>(Error.Forbidden);
        }

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(workspaceId), ct);
        if (access is null)
        {
            return new Result<UserId>(WorkspacesErrors.NotFound);
        }

        return access < needed ? new Result<UserId>(Error.Forbidden) : new Result<UserId>(user.UserId);
    }

    private async Task<ChatSummary> SummaryAsync(Chat chat, CancellationToken ct)
    {
        bool waiting = await db.Messages.AnyAsync(message => message.ChatId == chat.Id
            && (message.State == MessageState.New || message.State == MessageState.Queued || message.State == MessageState.Steering), ct);
        return chat.ToSummary(waiting);
    }
}
