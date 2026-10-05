namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// The nook's files after the turn were saved as one of its checkpoints, which can be downloaded or
/// started from (<see cref="StartChat.Checkpoint"/>), and from which the nook comes back if its
/// machine is lost.
/// </summary>
/// <param name="Number">The checkpoint's number in the chat's nook.</param>
public sealed record CheckpointSaved(int Number);
