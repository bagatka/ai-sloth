using System;
using System.Collections.Generic;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// What a chat's agents have used of their models: every call the model gateway saw, added up and by
/// turn, with what a cost is known for; how full the context is; and how long its prompt's cache stays.
/// </summary>
/// <param name="Calls">How many calls the chat's agents made.</param>
/// <param name="Tokens">Their tokens.</param>
/// <param name="Cost">What they cost in US dollars, as far as their prices are known; null when none is.</param>
/// <param name="Estimate">Whether the cost is a plan's estimate at the vendor's API prices.</param>
/// <param name="ContextUsed">Tokens in the context at the latest call, its input and answer.</param>
/// <param name="ContextWindow">The most tokens the chat's model's context holds, when known.</param>
/// <param name="CacheWarmUntil">Until when the prompt's cache stays, at least, when a call used one and the vendor's rule is known.</param>
/// <param name="RereadCost">What the next call costs to read the context again once the cache is gone, in US dollars, when known.</param>
/// <param name="Turns">The latest turns' usage, newest first, at most 50.</param>
public sealed record ChatUsage(
    int Calls,
    TokenCounts Tokens,
    decimal? Cost,
    bool Estimate,
    int? ContextUsed,
    int? ContextWindow,
    DateTimeOffset? CacheWarmUntil,
    decimal? RereadCost,
    IReadOnlyList<TurnUsage> Turns);
