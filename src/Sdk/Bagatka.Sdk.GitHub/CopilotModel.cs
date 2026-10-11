namespace Bagatka.Sdk.GitHub;

/// <summary>A model a Copilot plan can chat with.</summary>
/// <param name="Id">Its ID, as Copilot's tools take it, such as <c>claude-opus-5.5</c>.</param>
/// <param name="Name">Its name for people, such as <c>Claude Opus 5.5</c>.</param>
/// <param name="UsesTools">Whether it can call tools.</param>
public sealed record CopilotModel(string Id, string Name, bool UsesTools);
