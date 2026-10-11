using System.Collections.Generic;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// What a workspace's chats used of their models over its last days, from every call the model gateway
/// saw.
/// </summary>
/// <param name="Days">Each day, the oldest first, days without calls included.</param>
/// <param name="Chats">Each chat with calls in those days, with what they cost, the costliest first.</param>
/// <param name="Calls">How many calls all of them made.</param>
/// <param name="Tokens">Their tokens.</param>
/// <param name="Cost">What they cost in US dollars, as far as their prices are known.</param>
/// <param name="Estimate">Whether the cost includes plans' estimates at the vendors' API prices.</param>
public sealed record WorkspaceUsage(IReadOnlyList<DayUsage> Days, IReadOnlyList<ChatCost> Chats, int Calls, TokenCounts Tokens, decimal? Cost, bool Estimate);
