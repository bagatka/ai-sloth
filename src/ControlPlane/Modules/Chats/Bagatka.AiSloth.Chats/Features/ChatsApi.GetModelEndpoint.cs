using Bagatka.AiSloth.Chats.Data;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;
using Bagatka.ModelAccess;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<ModelEndpoint>> GetModelEndpointAsync(Actor actor, string token, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

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

        return new Result<ModelEndpoint>(account.Output.Endpoint);
    }
}
