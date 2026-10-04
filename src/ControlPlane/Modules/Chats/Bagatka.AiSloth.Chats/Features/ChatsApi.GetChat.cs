using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<ChatSummary>> GetAsync(Actor actor, ChatId id, CancellationToken ct)
    {
        Chat? chat = await FindChatAsync(actor, id, ct);
        return chat is null
            ? new Result<ChatSummary>(ChatsErrors.NotFound)
            : new Result<ChatSummary>(chat.ToSummary(await MessagesWaitingAsync(id, ct)));
    }
}
