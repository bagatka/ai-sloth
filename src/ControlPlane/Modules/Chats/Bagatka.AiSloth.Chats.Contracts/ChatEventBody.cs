namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// What happened in a chat. A message is sent, then either starts a turn, goes into the running
/// turn (steered), or is cancelled. A turn shows the agent's updates and ends with a stop reason.
/// </summary>
public union ChatEventBody(MessageSent, TurnStarted, MessageSteered, MessageCancelled, AgentUpdate, TurnEnded);
