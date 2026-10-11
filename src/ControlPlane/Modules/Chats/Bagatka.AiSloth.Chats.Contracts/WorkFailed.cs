namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// AiSloth's work on the chat failed with an error it doesn't expect, such as copying its nook's files
/// in or starting its agent, and it tries again every few seconds until <see cref="WorkResumed"/>. Said
/// once, however many tries fail; the host's log says what failed.
/// </summary>
public sealed record WorkFailed;
