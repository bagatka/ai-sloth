using System.Threading.Tasks;
using System.Threading;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation.Modules;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<ChatMessage>> SendAsync(Actor actor, SendMessage command, CancellationToken ct)
    {
        Result<Chat> found = await FindChatAsync(actor, command.ChatId, AccessLevel.Write, ct);
        if (found.Failed)
        {
            return new Result<ChatMessage>(found.Error);
        }

        // People write in chats; whoever may not use the chat's account proposes instead.
        if (actor is not UserActor user)
        {
            return new Result<ChatMessage>(Error.Forbidden);
        }

        // A removed account isn't anyone's to propose to: the message goes through, and its turn fails
        // with the reason.
        Chat chat = found.Output;
        bool? mayUseAccount = await accounts.MayUseAsync(actor, chat.AgentAccountId, chat.WorkspaceId, ct);
        bool isProposal = mayUseAccount == false;
        if (command.Proposal is not null)
        {
            if (isProposal)
            {
                return new Result<ChatMessage>(Error.Forbidden);
            }

            bool proposed = await db.Messages.AnyAsync(message => message.Id == command.Proposal && message.ChatId == chat.Id && message.State == MessageState.Proposed, ct);
            if (!proposed)
            {
                return new Result<ChatMessage>(Error.Validation("proposal", "The chat has no such proposal."));
            }
        }

        bool unconfirmed = false;
        if (!isProposal)
        {
            unconfirmed = await NeedsDiskConfirmationAsync(chat, command.ConfirmNearlyFullDisk, ct);
        }

        if (unconfirmed)
        {
            return new Result<ChatMessage>(ChatsErrors.DiskNearlyFull);
        }

        Result<Message> sent = Message.Send(command.ChatId, user.UserId, command.Text, isProposal, command.Proposal, setupTest: null, time);
        if (sent.Failed)
        {
            return new Result<ChatMessage>(sent.Error);
        }

        // Recorded only; the chat's runner announces and delivers it.
        Message message = sent.Output;
        db.Messages.Add(message);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<ChatMessage>(saved.Error);
        }

        runners.Wake(command.ChatId);
        return new Result<ChatMessage>(message.ToContract());
    }

    // A nearly full disk is the sender's to confirm, because the agent's work and checkpoints may
    // fail: a message for the agent, unconfirmed, while the nook's disk was nearly full at its
    // daemon's last report, needs it. Proposals don't run the agent, so they never ask.
    private async Task<bool> NeedsDiskConfirmationAsync(Chat chat, bool confirmed, CancellationToken ct)
    {
        if (confirmed)
        {
            return false;
        }

        Result<NookSummary> nook = await nooks.GetAsync(SystemActors.Harness, chat.NookId, ct);
        return !nook.Failed && nook.Output.Disk is DiskUsage disk && disk.TotalBytes > 0
            && 1 - ((double)disk.AvailableBytes / disk.TotalBytes) >= settings.NearlyFullDisk;
    }
}
