using System;

namespace Bagatka.Harnesses;

/// <summary>The turn a <see cref="Acp.Prompt"/> started ended.</summary>
/// <param name="Prompt">The key the prompt was sent with.</param>
/// <param name="StopReason">Why it ended, such as <c>end_turn</c> or <c>cancelled</c>.</param>
public sealed record AcpPromptEnded(Guid Prompt, string StopReason);
