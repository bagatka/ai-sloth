using System;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>What one day's model calls in a workspace used and cost.</summary>
/// <param name="Day">The day, in the time zone asked for.</param>
/// <param name="Calls">How many calls were made.</param>
/// <param name="Tokens">Their tokens.</param>
/// <param name="Cost">What they cost in US dollars, as far as their prices are known.</param>
/// <param name="Estimate">Whether the cost includes plans' estimates at the vendors' API prices.</param>
public sealed record DayUsage(DateOnly Day, int Calls, TokenCounts Tokens, decimal? Cost, bool Estimate);
