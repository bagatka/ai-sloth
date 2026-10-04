using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result> AuthorizeModelCallAsync(Actor actor, string token, CancellationToken ct)
    {
        // The token is the credential; the actor is always anonymous.
        byte[] hash = Chat.HashToken(token);
        return await db.Chats.AnyAsync(chat => chat.HarnessTokenHash == hash, ct)
            ? new Result(new Success())
            : new Result(Error.Unauthorized);
    }
}
