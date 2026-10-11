using Bagatka.AiSloth.Chats.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    // The latest calls, of which the largest is the conversation's own: an agent's side calls, such
    // as Claude Code's for a title, read little.
    private const int LatestCalls = 10;
    private const int LatestTurns = 50;

    public async Task<Result<ChatUsage>> GetUsageAsync(Actor actor, ChatId id, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Chat> found = await FindChatAsync(db, actor, id, AccessLevel.Read, ct);
        if (found.Failed)
        {
            return new Result<ChatUsage>(found.Error);
        }

        // By turn, the calls between turns together; the chat's whole is all of them.
        Chat chat = found.Output;
        IQueryable<ModelCall> calls = db.ModelCalls.AsNoTracking().Where(call => call.ChatId == id);
        List<Spent<MessageId?>> spent = await calls.GroupBy(call => call.Turn).Select(SpentOf<MessageId?>()).ToListAsync(ct);
        List<ModelCall> latest = await calls.OrderByDescending(call => call.At).Take(LatestCalls).ToListAsync(ct);
        Spent<MessageId?> total = Spent<MessageId?>.Sum(key: null, spent);

        ModelCall? conversation = latest.MaxBy(call => call.WholeInput);
        ModelFacts facts = await FactsOfChatModelAsync(chat, ct);
        decimal? reread = conversation is null ? null : facts.Price?.CostOf(conversation.WholeInput, 0, 0, 0, 0);
        List<TurnUsage> turns = [.. spent
            .Where(turn => turn.Key is not null)
            .OrderByDescending(turn => turn.Last)
            .Take(LatestTurns)
            .Select(turn => new TurnUsage(turn.Key!.Value, turn.Calls, turn.Tokens(), turn.Cost(), turn.Estimate))];
        ChatUsage usage = new ChatUsage(
            total.Calls,
            total.Tokens(),
            total.Cost(),
            total.Estimate,
            conversation is null ? null : conversation.WholeInput + conversation.Output,
            facts.ContextWindow,
            latest.Max(call => call.CacheWarmUntil),
            reread,
            turns);
        return new Result<ChatUsage>(usage);
    }

    // What's known of the chat's model; nothing when the chat has none yet or its account is gone.
    private async Task<ModelFacts> FactsOfChatModelAsync(Chat chat, CancellationToken ct)
    {
        if (chat.Model is not string model)
        {
            return ModelFacts.Unknown;
        }

        Result<ModelFacts> facts = await accounts.GetModelFactsAsync(SystemActors.Harness, chat.AgentAccountId, chat.WorkspaceId, model, ct);
        return facts.Failed ? ModelFacts.Unknown : facts.Output;
    }
}
