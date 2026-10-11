using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    // Model calls added up in the database, by a turn, a chat, or a day: one rule for every usage.
    private static Expression<Func<IGrouping<TKey, ModelCall>, Spent<TKey>>> SpentOf<TKey>()
    {
        return group => new Spent<TKey>(
            group.Key, group.Count(), group.Sum(call => (long)call.Input), group.Sum(call => (long)call.CacheRead), group.Sum(call => (long)call.CacheWrite + call.CacheWriteLong),
            group.Sum(call => (long)call.Output), group.Sum(call => (long)call.Reasoning), group.Sum(call => call.Cost), group.Count(call => call.Cost != null), group.Any(call => call.Estimate), group.Max(call => call.At));
    }

    // Calls added up: their tokens, their known costs, and whether any is an estimate. A cost is known
    // once any call's is: calls of an unpriced model add nothing to it.
    private sealed record Spent<TKey>(TKey Key, int Calls, long Input, long CacheRead, long CacheWrite, long Output, long Reasoning, decimal? CostSum, int Priced, bool Estimate, DateTimeOffset Last)
    {
        public TokenCounts Tokens()
        {
            return new TokenCounts(Input, CacheRead, CacheWrite, Output, Reasoning);
        }

        public decimal? Cost()
        {
            return Priced > 0 ? CostSum : null;
        }

        public static Spent<TKey> Sum(TKey key, IReadOnlyList<Spent<TKey>> all)
        {
            return new Spent<TKey>(
                key,
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
