namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>What one turn's model calls used and cost.</summary>
/// <param name="Turn">The message the turn answered.</param>
/// <param name="Calls">How many calls it made.</param>
/// <param name="Tokens">Their tokens.</param>
/// <param name="Cost">What they cost in US dollars, as far as their prices are known.</param>
/// <param name="Estimate">Whether the cost is a plan's estimate at the vendor's API prices.</param>
public sealed record TurnUsage(MessageId Turn, int Calls, TokenCounts Tokens, decimal? Cost, bool Estimate);
