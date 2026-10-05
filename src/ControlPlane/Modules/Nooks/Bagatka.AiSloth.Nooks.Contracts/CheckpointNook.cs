namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>Input to <see cref="INooksApi.CheckpointAsync"/>.</summary>
/// <param name="NookId">The nook.</param>
/// <param name="Note">What the checkpoint follows, in words for people, such as the message whose turn just ended; at most 200 characters.</param>
public sealed record CheckpointNook(NookId NookId, string Note);
