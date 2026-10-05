using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Harness;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Harnesses;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<ChatSummary>> StartAsync(Actor actor, StartChat command, CancellationToken ct)
    {
        // Only people start chats, and only where they may write.
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(command.WorkspaceId), ct);
        if (access is null)
        {
            return new Result<ChatSummary>(WorkspacesErrors.NotFound);
        }

        if (actor is not UserActor user || access < AccessLevel.Write)
        {
            return new Result<ChatSummary>(Error.Forbidden);
        }

        HarnessProfile? harness = HarnessProfiles.Find(command.Harness);
        if (harness is null)
        {
            return new Result<ChatSummary>(Error.Validation("harness", "Unknown harness; see GET /harnesses."));
        }

        Error unusable = Error.Validation("account", "Use the workspace's account or your own, of a kind the harness takes.");
        Result<AgentAccountCredential> account = await accounts.UseAsync(actor, command.Account, command.WorkspaceId, ct);
        if (account.Failed)
        {
            return new Result<ChatSummary>(unusable);
        }

        bool harnessTakesIt = harness.Accepts(AccountCredentials.KindOf(account.Output.Kind));
        if (!harnessTakesIt)
        {
            return new Result<ChatSummary>(unusable);
        }

        // A copy of another chat is of its nook's files; Nooks decides whether the actor may see them.
        NookId? copyOf = await db.Chats.AsNoTracking().Where(found => found.Id == command.CopyOf).Select(found => (NookId?)found.NookId).SingleOrDefaultAsync(ct);
        if (command.CopyOf is not null && copyOf is null)
        {
            return new Result<ChatSummary>(Error.Validation("copyOf", "Must be another chat of the workspace."));
        }

        // Every chat gets a nook of its own, so its agent never works on another agent's files. The nook
        // stands on its own: should saving the chat fail, it stays until someone deletes it. Its
        // checkpoints keep the agent's sessions, so a new agent can continue the conversation.
        CreateNook nook = new CreateNook(command.WorkspaceId, command.Provider, harness.Id, command.Repositories, copyOf, command.Checkpoint, harness.SessionPaths);
        Result<NookSummary> created = await nooks.CreateAsync(actor, nook, ct);
        if (created.Failed)
        {
            return new Result<ChatSummary>(created.Error);
        }

        Chat chat = Chat.Start(created.Output.Id, command.WorkspaceId, user.UserId, harness.Id, account.Output, time);
        db.Chats.Add(chat);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<ChatSummary>(saved.Error);
        }

        // The agent starts now, after the nook's setup, while people write their first message.
        runners.Wake(chat.Id);
        return new Result<ChatSummary>(chat.ToSummary(messagesWaiting: false));
    }
}
