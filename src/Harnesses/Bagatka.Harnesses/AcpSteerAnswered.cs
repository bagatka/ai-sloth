using System;

namespace Bagatka.Harnesses;

/// <summary>The agent answered a <see cref="Acp.Steer"/>.</summary>
/// <param name="Message">The key the message was steered with.</param>
/// <param name="Injected">Whether it joined the running turn; otherwise no turn runs, and it needs a prompt.</param>
public sealed record AcpSteerAnswered(Guid Message, bool Injected);
