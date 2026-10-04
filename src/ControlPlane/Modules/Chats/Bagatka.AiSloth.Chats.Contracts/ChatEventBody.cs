namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// What happened in a chat. A message is sent, then either starts a turn, goes into the running
/// turn (steered), or is cancelled. A turn shows the agent's updates and ends with a stop reason. A
/// proposal stays with the people in the chat until the account's owner sends it on.
/// </summary>
public union ChatEventBody(MessageSent, MessageProposed, TurnStarted, MessageSteered, MessageCancelled, AgentUpdate, TurnEnded);
