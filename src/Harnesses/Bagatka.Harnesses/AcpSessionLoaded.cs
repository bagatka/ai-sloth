namespace Bagatka.Harnesses;

/// <summary>The agent loaded the earlier session (<see cref="Acp.LoadSession"/>); it continues the conversation.</summary>
/// <param name="Model">The model the session uses, when the agent says.</param>
public sealed record AcpSessionLoaded(string? Model);
