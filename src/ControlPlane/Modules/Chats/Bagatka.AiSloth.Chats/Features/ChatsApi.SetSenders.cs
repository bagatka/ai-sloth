using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result> SetSendersAsync(Actor actor, SetChatSenders command, CancellationToken ct)
    {
        Chat? chat = await FindChatAsync(actor, command.ChatId, ct);
        if (chat is null)
        {
            return new Result(ChatsErrors.NotFound);
        }

        if (chat.AccountOwnerId is null)
        {
            return new Result(Error.Validation("chatId", "Every member may send to a chat on the workspace's account."));
        }

        UserId owner = chat.AccountOwnerId.Value;
        bool isOwner = actor.Is(owner);
        if (!isOwner)
        {
            return new Result(Error.Forbidden);
        }

        List<UserId> members = [.. command.Members.Where(member => member != owner).Distinct()];
        if (members.Count > ChatSender.MaxPerChat)
        {
            return new Result(Error.Validation("members", string.Create(CultureInfo.InvariantCulture, $"At most {ChatSender.MaxPerChat} members.")));
        }

        // Plans are for one person: letting others in needs an account the deployment lets its owner share.
        if (members.Count > 0)
        {
            Result<IReadOnlyList<AgentAccountSummary>> owned = await accounts.ListAsync(actor, chat.WorkspaceId, ct);
            bool shareable = !owned.Failed && owned.Output.Any(account => account.Id == chat.AgentAccountId && account.Shareable);
            if (!shareable)
            {
                return new Result(Error.Validation("members", "This chat's account is for its owner alone."));
            }
        }

        List<ChatSender> previous = await db.Senders.Where(sender => sender.ChatId == chat.Id).ToListAsync(ct);
        db.Senders.RemoveRange(previous);
        db.Senders.AddRange(members.Select(member => new ChatSender(chat.Id, member)));
        return await db.SaveAsync(ct);
    }
}
