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
        if (chat is null)
        {
            return new Result<ChatSummary>(ChatsErrors.NotFound);
        }

        ChatSummary summary = await SummaryAsync(chat, ct);
        return new Result<ChatSummary>(summary);
    }
}
