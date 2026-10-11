namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>What one chat's model calls cost over some days.</summary>
/// <param name="Chat">The chat.</param>
/// <param name="Calls">How many calls its agents made then.</param>
/// <param name="Cost">What they cost in US dollars, as far as their prices are known.</param>
/// <param name="Estimate">Whether the cost is a plan's estimate at the vendor's API prices.</param>
public sealed record ChatCost(ChatId Chat, int Calls, decimal? Cost, bool Estimate);
