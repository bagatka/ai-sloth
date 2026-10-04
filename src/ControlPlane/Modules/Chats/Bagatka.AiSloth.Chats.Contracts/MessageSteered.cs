namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>The message went into the running turn, so the agent reads it while it works.</summary>
/// <param name="MessageId">The message.</param>
public sealed record MessageSteered(MessageId MessageId);
