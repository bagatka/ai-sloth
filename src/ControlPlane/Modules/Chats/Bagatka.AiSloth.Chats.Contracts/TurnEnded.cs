namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// The turn ended. The stop reason is the agent's (<c>end_turn</c>, <c>max_tokens</c>,
/// <c>max_turn_requests</c>, <c>refusal</c>, or <c>cancelled</c>), or <c>failed</c> when the agent
/// couldn't start, stopped, or errored before finishing.
/// </summary>
/// <param name="StopReason">Why it ended.</param>
/// <param name="Failure">What went wrong, in words for people, when it failed; otherwise <see langword="null"/>.</param>
public sealed record TurnEnded(string StopReason, string? Failure);
