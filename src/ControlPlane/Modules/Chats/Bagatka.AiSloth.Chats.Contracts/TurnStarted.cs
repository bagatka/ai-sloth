namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>The agent started a turn to answer the message.</summary>
/// <param name="MessageId">The message.</param>
public sealed record TurnStarted(MessageId MessageId);
