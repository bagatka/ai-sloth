using Bagatka.AiSloth.Chats.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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
        List<Spent> spent = await calls.GroupBy(call => call.Turn).Select(SpentOfTurn).ToListAsync(ct);
        List<ModelCall> latest = await calls.OrderByDescending(call => call.At).Take(LatestCalls).ToListAsync(ct);
        Spent total = Spent.Sum(spent);

        ModelCall? conversation = latest.MaxBy(call => call.WholeInput);
        ModelFacts facts = await FactsOfChatModelAsync(chat, ct);
        decimal? reread = conversation is null ? null : facts.Price?.CostOf(conversation.WholeInput, 0, 0, 0, 0);
        List<TurnUsage> turns = [.. spent
            .Where(turn => turn.Turn is not null)
            .OrderByDescending(turn => turn.Last)
            .Take(LatestTurns)
            .Select(turn => new TurnUsage(turn.Turn!.Value, turn.Calls, turn.Tokens(), turn.Cost(), turn.Estimate))];
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

    private static readonly Expression<Func<IGrouping<MessageId?, ModelCall>, Spent>> SpentOfTurn = group => new Spent(
        group.Key, group.Count(), group.Sum(call => (long)call.Input), group.Sum(call => (long)call.CacheRead), group.Sum(call => (long)call.CacheWrite + call.CacheWriteLong),
        group.Sum(call => (long)call.Output), group.Sum(call => (long)call.Reasoning), group.Sum(call => call.Cost), group.Count(call => call.Cost != null), group.Any(call => call.Estimate), group.Max(call => call.At));

    // Calls added up: their tokens, their known costs, and whether any is an estimate.
    private sealed record Spent(MessageId? Turn, int Calls, long Input, long CacheRead, long CacheWrite, long Output, long Reasoning, decimal? CostSum, int Priced, bool Estimate, DateTimeOffset Last)
    {
        public TokenCounts Tokens()
        {
            return new TokenCounts(Input, CacheRead, CacheWrite, Output, Reasoning);
        }

        public decimal? Cost()
        {
            return Priced > 0 ? CostSum : null;
        }

        public static Spent Sum(IReadOnlyList<Spent> all)
        {
            return new Spent(
                Turn: null,
                all.Sum(spent => spent.Calls),
                all.Sum(spent => spent.Input),
                all.Sum(spent => spent.CacheRead),
                all.Sum(spent => spent.CacheWrite),
                all.Sum(spent => spent.Output),
                all.Sum(spent => spent.Reasoning),
                all.Sum(spent => spent.CostSum ?? 0),
                all.Sum(spent => spent.Priced),
                all.Any(spent => spent.Estimate),
                all.Count > 0 ? all.Max(spent => spent.Last) : DateTimeOffset.MinValue);
        }
    }
}
