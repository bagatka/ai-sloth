using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<string>> GetModelKeyAsync(Actor actor, string token, CancellationToken ct)
    {
        // The token is the credential; the actor is always anonymous.
        byte[] hash = Chat.HashToken(token);
        Chat? chat = await db.Chats.AsNoTracking().SingleOrDefaultAsync(found => found.HarnessTokenHash == hash, ct);
        if (chat is null)
        {
            return new Result<string>(Error.Unauthorized);
        }

        Result<AgentAccountCredential> account = await accounts.UseAsync(SystemActors.Harness, chat.AgentAccountId, chat.WorkspaceId, ct);
        if (account.Failed)
        {
            return new Result<string>(Error.Unauthorized);
        }

        return new Result<string>(account.Output.Secret);
    }
}
