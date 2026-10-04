using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result> StopAsync(Actor actor, ChatId id, CancellationToken ct)
    {
        if (await FindChatAsync(actor, id, ct) is null)
        {
            return new Result(ChatsErrors.NotFound);
        }

        // Not handled: a restart between this call and the runner taking it loses the stop; the
        // person stops again.
        runners.Stop(id);
        return new Result(new Success());
    }
}
