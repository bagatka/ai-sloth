namespace Bagatka.Harnesses;

/// <summary>The agent started the session <see cref="Acp.NewSession"/> asked for; prompts go to it.</summary>
/// <param name="SessionId">The session's ID.</param>
/// <param name="Model">The model the session uses, when the agent says.</param>
public sealed record AcpSessionCreated(string SessionId, string? Model);
