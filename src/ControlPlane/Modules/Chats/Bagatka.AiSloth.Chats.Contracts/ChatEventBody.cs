namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// What happened in a chat. A message is sent, then either starts a turn, goes into the running
/// turn (steered), or is cancelled. A turn shows the agent's updates and ends with a stop reason,
/// then a checkpoint of the nook's files. A proposal stays with the people in the chat until the
/// account's owner sends it on. An agent that had to be started again says whether it remembers.
/// The project's setup, when it has one, starts and ends before its agent starts; a setup the agent
/// prepared is tested in a fresh nook after its turn.
/// </summary>
public union ChatEventBody(MessageSent, MessageProposed, TurnStarted, MessageSteered, MessageCancelled, AgentUpdate, TurnEnded, CheckpointSaved, CheckpointFailed, AgentRestarted, SetupStarted, SetupEnded, SetupTestStarted, SetupTested);
