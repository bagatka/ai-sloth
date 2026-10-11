namespace Bagatka.Sdk.Anthropic;

/// <summary>A model Anthropic's API serves.</summary>
/// <param name="Id">Its ID in calls, such as <c>claude-opus-5-5</c>.</param>
/// <param name="DisplayName">Its name for people, such as <c>Claude Opus 5.5</c>.</param>
public sealed record AnthropicModel(string Id, string DisplayName);
