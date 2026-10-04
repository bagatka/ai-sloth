using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<ChatMessage>> SendAsync(Actor actor, SendMessage command, CancellationToken ct)
    {
        Chat? chat = await FindChatAsync(actor, command.ChatId, ct);
        if (chat is null)
        {
            return new Result<ChatMessage>(ChatsErrors.NotFound);
        }

        // Members are people; on a personal account, only its owner and whoever they let in.
        if (actor is not UserActor user)
        {
            return new Result<ChatMessage>(Error.Forbidden);
        }

        bool letIn = await db.Senders.AnyAsync(sender => sender.ChatId == chat.Id && sender.UserId == user.UserId, ct);
        bool maySend = chat.OpenTo(user.UserId) || letIn;
        if (!maySend)
        {
            return new Result<ChatMessage>(Error.Forbidden);
        }

        Result<Message> sent = Message.Send(command.ChatId, user.UserId, command.Text, time);
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
}
