namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>Saving the nook's files after the turn failed; the next turn's checkpoint tries again.</summary>
/// <param name="Failure">What went wrong, in words for people.</param>
public sealed record CheckpointFailed(string Failure);
