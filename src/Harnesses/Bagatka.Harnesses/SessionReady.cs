namespace Bagatka.Harnesses;

/// <summary>The agent has a session, and takes prompts.</summary>
/// <param name="Model">The model it starts on, when the agent says, such as <c>gpt-5-codex</c>, or <c>default</c> for its own choice.</param>
/// <param name="Remembers">
/// Whether it continues the earlier session, remembering the conversation: true when it loaded it,
/// false when it couldn't and started a new one; null when there was none.
/// </param>
public sealed record SessionReady(string? Model, bool? Remembers);
