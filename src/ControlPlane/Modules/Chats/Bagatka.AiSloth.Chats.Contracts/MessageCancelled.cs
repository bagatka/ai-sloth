namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>Someone stopped the agent before the message reached it; the agent never saw it.</summary>
/// <param name="MessageId">The message.</param>
public sealed record MessageCancelled(MessageId MessageId);
