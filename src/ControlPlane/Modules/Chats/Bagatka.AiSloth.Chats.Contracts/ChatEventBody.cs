namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// What happened in a chat. A message is sent, then either starts a turn, goes into the running
/// turn (steered), or is cancelled. A turn shows the agent's updates and ends with a stop reason,
/// then a checkpoint of the nook's files. A proposal stays with the people in the chat until the
/// account's owner sends it on. An agent that had to be started again says whether it remembers.
/// Someone may change the agent's model or account, from the next turn on.
/// The nook's setup, when its files have one, starts and ends before its agent starts; someone may ask
/// to run it again, which it does once the agent answered what was sent before, or start a chat that
/// fixes it. When AiSloth's own work on the chat keeps failing, the chat says so until it goes on.
/// </summary>
public union ChatEventBody(MessageSent, MessageProposed, TurnStarted, MessageSteered, MessageCancelled, AgentUpdate, TurnEnded, CheckpointSaved, CheckpointFailed, AgentRestarted, ModelChanged, SetupStarted, SetupEnded, SetupRunAsked, SetupFixStarted, DiskNearlyFull, WorkFailed, WorkResumed);
