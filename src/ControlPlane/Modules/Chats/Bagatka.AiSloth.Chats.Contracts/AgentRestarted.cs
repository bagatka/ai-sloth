namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// The agent's process had ended, such as when the nook's machine was lost, and a new agent took
/// over for the next message.
/// </summary>
/// <param name="Remembers">
/// Whether it continues the conversation, from the session its harness kept in the nook; otherwise
/// it starts without the earlier messages.
/// </param>
public sealed record AgentRestarted(bool Remembers);
