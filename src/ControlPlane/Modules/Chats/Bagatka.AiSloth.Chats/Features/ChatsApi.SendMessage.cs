using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<ChatMessage>> SendAsync(Actor actor, SendMessage command, CancellationToken ct)
    {
        if (await FindChatAsync(actor, command.ChatId, ct) is null)
        {
            return new Result<ChatMessage>(ChatsErrors.NotFound);
        }

        // Members are people; only they send messages.
        if (actor.Value is not UserActor user)
        {
            return new Result<ChatMessage>(Error.Forbidden);
        }

        if (!Message.Send(command.ChatId, user.UserId, command.Text, time).TryGetValue(out Message? message, out Error? invalid))
        {
            return new Result<ChatMessage>(invalid);
        }

        // Recorded only; the chat's runner announces and delivers it.
        db.Messages.Add(message);
        if ((await db.SaveAsync(ct)).IsError(out Error? failed))
        {
            return new Result<ChatMessage>(failed);
        }

        runners.Wake(command.ChatId);
        return new Result<ChatMessage>(message.ToContract());
    }
}
