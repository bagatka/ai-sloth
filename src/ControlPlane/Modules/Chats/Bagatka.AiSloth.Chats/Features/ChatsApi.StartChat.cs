using System.Threading.Tasks;
using System.Threading;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Harness;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation.Modules;
using Bagatka.Foundation;
using Bagatka.Harnesses;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<ChatSummary>> StartAsync(Actor actor, StartChat command, CancellationToken ct)
    {
        Result<NookSummary> found = await nooks.GetAsync(actor, command.NookId, ct);
        if (found.Failed)
        {
            return new Result<ChatSummary>(found.Error);
        }

        NookSummary nook = found.Output;

        // Only people start chats, and only where they may write.
        if (actor is not UserActor user)
        {
            return new Result<ChatSummary>(Error.Forbidden);
        }

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Nook(nook.Id.Value), ct);
        if (access is null || access < AccessLevel.Write)
        {
            return new Result<ChatSummary>(Error.Forbidden);
        }

        HarnessProfile? harness = nook.Harness is null ? null : HarnessProfiles.Find(nook.Harness);
        if (harness is null)
        {
            return new Result<ChatSummary>(Error.Validation("nookId", "The nook carries no harness for an agent; create one that does."));
        }

        Error unusable = Error.Validation("account", "Use the workspace's account or your own, of a kind the harness takes.");
        Result<AgentAccountCredential> account = await accounts.UseAsync(actor, command.Account, nook.WorkspaceId, ct);
        if (account.Failed)
        {
            return new Result<ChatSummary>(unusable);
        }

        bool harnessTakesIt = harness.Accepts(AccountCredentials.KindOf(account.Output.Kind));
        if (!harnessTakesIt)
        {
            return new Result<ChatSummary>(unusable);
        }

        Chat chat = Chat.Start(nook.Id, nook.WorkspaceId, user.UserId, harness.Id, account.Output, time);
        db.Chats.Add(chat);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<ChatSummary>(saved.Error);
        }

        return new Result<ChatSummary>(chat.ToSummary(messagesWaiting: false));
    }
}
