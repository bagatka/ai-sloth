using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<ChatSummary>> StartAsync(Actor actor, StartChat command, CancellationToken ct)
    {
        if (!(await nooks.GetAsync(actor, command.NookId, ct)).TryGetValue(out NookSummary? nook, out Error? missing))
        {
            return new Result<ChatSummary>(missing);
        }

        // Only people start chats.
        if (actor.Value is not UserActor user)
        {
            return new Result<ChatSummary>(Error.Forbidden);
        }

        Chat chat = Chat.Start(nook.Id, nook.WorkspaceId, user.UserId, time);
        db.Chats.Add(chat);
        if ((await db.SaveAsync(ct)).IsError(out Error? failed))
        {
            return new Result<ChatSummary>(failed);
        }

        return new Result<ChatSummary>(chat.ToSummary(messagesWaiting: false));
    }
}
