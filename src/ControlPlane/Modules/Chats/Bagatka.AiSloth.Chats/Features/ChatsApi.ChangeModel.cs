using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Harnesses;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<ChatSummary>> ChangeModelAsync(Actor actor, ChangeModel command, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Chat> found = await FindChatAsync(db, actor, command.ChatId, AccessLevel.Write, ct);
        if (found.Failed)
        {
            return new Result<ChatSummary>(found.Error);
        }

        if (actor is not UserActor user)
        {
            return new Result<ChatSummary>(Error.Forbidden);
        }

        Result<HarnessProfile> harness = HarnessWith(found.Output.Harness, command.Model, command.Thinking);
        if (harness.Failed)
        {
            return new Result<ChatSummary>(harness.Error);
        }

        Result<AgentAccountSummary> account = await AccountForAsync(actor, found.Output.WorkspaceId, command.Account, harness.Output, ct);
        if (account.Failed)
        {
            return new Result<ChatSummary>(account.Error);
        }

        // Whose the account is decides who may direct the agent and whom its nook is reserved for, so
        // it stays: the workspace's, or the same person's own.
        if (account.Output.OwnerId != found.Output.AccountOwnerId)
        {
            string message = found.Output.AccountOwnerId is null
                ? "This chat runs on the workspace's accounts; choose another of them."
                : "This chat runs on its owner's own accounts; choose another of theirs.";
            return new Result<ChatSummary>(Error.Validation("account", message));
        }

        Result<Chat> changed = await ChangeChatAsync(
            db,
            command.ChatId,
            chat => chat.ChangeAgent(command.Account, command.Model, command.Thinking),
            new ChatEventBody(new ModelChanged(command.Account, command.Model, command.Thinking, user.UserId)),
            ct);
        if (changed.Failed)
        {
            return new Result<ChatSummary>(changed.Error);
        }

        runners.Wake(command.ChatId);
        signals.Notify(command.ChatId);
        signals.Notify(changed.Output.WorkspaceId);
        ChatSummary summary = await SummaryAsync(db, changed.Output, ct);
        return new Result<ChatSummary>(summary);
    }
}
