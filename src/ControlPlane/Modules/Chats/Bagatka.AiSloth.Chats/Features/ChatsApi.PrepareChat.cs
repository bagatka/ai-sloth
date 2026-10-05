using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Harness;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<ChatMessage>> PrepareAsync(Actor actor, PrepareChat command, CancellationToken ct)
    {
        Result<Chat> found = await FindChatAsync(actor, command.ChatId, AccessLevel.Write, ct);
        if (found.Failed)
        {
            return new Result<ChatMessage>(found.Error);
        }

        // Preparing runs the agent, and its tests spend the person's compute, so only someone who may
        // use the chat's account prepares; others propose what to do.
        Chat chat = found.Output;
        bool? mayUseAccount = await accounts.MayUseAsync(actor, chat.AgentAccountId, chat.WorkspaceId, ct);
        if (actor is not UserActor user || mayUseAccount != true)
        {
            return new Result<ChatMessage>(Error.Forbidden);
        }

        bool unconfirmed = await NeedsDiskConfirmationAsync(chat, command.ConfirmNearlyFullDisk, ct);
        if (unconfirmed)
        {
            return new Result<ChatMessage>(ChatsErrors.DiskNearlyFull);
        }

        // Its turn's end calls for the first test of the setup.
        Result<Message> sent = Message.Send(command.ChatId, user.UserId, ProjectSetups.PrepareRequest, isProposal: false, proposalId: null, setupTest: 1, time);
        if (sent.Failed)
        {
            return new Result<ChatMessage>(sent.Error);
        }

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
}
