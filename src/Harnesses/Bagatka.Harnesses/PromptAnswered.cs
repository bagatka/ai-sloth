using System;

namespace Bagatka.Harnesses;

/// <summary>The turn a prompt started ended (<see cref="AgentSession.Prompt"/>).</summary>
/// <param name="Prompt">The prompt's key.</param>
/// <param name="StopReason">Why it ended, such as <c>end_turn</c> or <c>cancelled</c>; <c>failed</c> with an error.</param>
/// <param name="Error">The agent's error, in words for people, when it failed.</param>
public sealed record PromptAnswered(Guid Prompt, string StopReason, string? Error);
