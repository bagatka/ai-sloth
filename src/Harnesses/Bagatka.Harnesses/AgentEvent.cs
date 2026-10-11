namespace Bagatka.Harnesses;

/// <summary>
/// What happened in an agent's session (<see cref="AgentSession.Read"/>): progress of the running
/// turn, the session ready, a prompt or a steer answered, or an agent that can't start.
/// </summary>
public union AgentEvent(AgentReported, SessionReady, PromptAnswered, SteerAnswered, StartFailed);
