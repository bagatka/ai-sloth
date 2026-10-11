using System;

namespace Bagatka.Harnesses;

/// <summary>The agent answered a message offered to the running turn (<see cref="AgentSession.Steer"/>).</summary>
/// <param name="Message">The message's key.</param>
/// <param name="Joined">Whether it joined the running turn; otherwise no turn runs, and it needs a prompt.</param>
public sealed record SteerAnswered(Guid Message, bool Joined);
