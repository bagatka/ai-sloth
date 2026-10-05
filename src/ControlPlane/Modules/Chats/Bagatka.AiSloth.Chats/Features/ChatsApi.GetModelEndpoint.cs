using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<ModelEndpoint>> GetModelEndpointAsync(Actor actor, string token, CancellationToken ct)
    {
        // The token is the credential; the actor is always anonymous.
        byte[] hash = Chat.HashToken(token);
        Chat? chat = await db.Chats.AsNoTracking().SingleOrDefaultAsync(found => found.HarnessTokenHash == hash, ct);
        if (chat is null)
        {
            return new Result<ModelEndpoint>(Error.Unauthorized);
        }

        Result<AgentAccountCredential> account = await accounts.UseAsync(SystemActors.Harness, chat.AgentAccountId, chat.WorkspaceId, ct);
        if (account.Failed)
        {
            return new Result<ModelEndpoint>(account.Error);
        }

        // A plan's token held by its harness never comes through the gateway.
        if (account.Output.Access is not ModelEndpoint endpoint)
        {
            return new Result<ModelEndpoint>(Error.Unauthorized);
        }

        return new Result<ModelEndpoint>(endpoint);
    }
}
