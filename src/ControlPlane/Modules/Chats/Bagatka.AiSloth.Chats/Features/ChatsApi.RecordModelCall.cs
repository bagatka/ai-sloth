using Bagatka.AiSloth.Chats.Data;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result> RecordModelCallAsync(Actor actor, string token, ModelCallUsage usage, DateTimeOffset startedAt, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        // The token is the credential; the actor is always anonymous.
        byte[] hash = Chat.HashToken(token);
        Chat? chat = await db.Chats.AsNoTracking().SingleOrDefaultAsync(found => found.HarnessTokenHash == hash, ct);
        if (chat is null)
        {
            return new Result(Error.Unauthorized);
        }

        // The turn the call started in: the chat's last to start before it, unless it ended first. The
        // chat may have moved on by the time the answer is in.
        StoredEvent? boundary = await db.Events.AsNoTracking()
            .Where(found => found.ChatId == chat.Id && found.At <= startedAt && (found.Kind == "turn-started" || found.Kind == "turn-ended"))
            .OrderByDescending(found => found.Sequence)
            .FirstOrDefaultAsync(ct);
        MessageId? turn = boundary?.ToContract().Body is TurnStarted started ? started.MessageId : null;

        // Priced as the chat's account pays for the model that answered; unpriced when the account is
        // gone meanwhile.
        Result<ModelFacts> facts = await accounts.GetModelFactsAsync(SystemActors.Harness, chat.AgentAccountId, chat.WorkspaceId, usage.Model, ct);
        db.ModelCalls.Add(ModelCall.Record(chat, turn, startedAt, usage, facts.Failed ? ModelFacts.Unknown : facts.Output, time));
        return await db.SaveAsync(ct);
    }
}
