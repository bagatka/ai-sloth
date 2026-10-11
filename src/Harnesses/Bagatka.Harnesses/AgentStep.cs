using System.Collections.Generic;

namespace Bagatka.Harnesses;

/// <summary>What a line the harness wrote came to (<see cref="AgentSession.Read"/>).</summary>
/// <param name="Session">The session now, which the caller keeps.</param>
/// <param name="Lines">The lines to write to the harness, in order.</param>
/// <param name="Event">What happened, for the caller to act on; null for nothing it needs.</param>
public sealed record AgentStep(AgentSession Session, IReadOnlyList<string> Lines, AgentEvent? Event);
