using Bagatka.AiSloth.Chats.Data;
using System.Threading.Tasks;
using System.Threading;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<ChatSummary>> GetAsync(Actor actor, ChatId id, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Chat> chat = await FindChatAsync(db, actor, id, AccessLevel.Read, ct);
        if (chat.Failed)
        {
            return new Result<ChatSummary>(chat.Error);
        }

        ChatSummary summary = await SummaryAsync(db, chat.Output, ct);
        return new Result<ChatSummary>(summary);
    }
}
