using System.Threading.Tasks;
using System.Threading;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result> StopAsync(Actor actor, ChatId id, CancellationToken ct)
    {
        Result<Chat> chat = await FindChatAsync(actor, id, AccessLevel.Write, ct);
        if (chat.Failed)
        {
            return new Result(chat.Error);
        }

        // Not handled: a restart between this call and the runner taking it loses the stop, and so
        // does an instance that isn't active yet, during a deploy's handover; the person stops again.
        runners.Stop(id);
        return new Result(new Success());
    }
}
