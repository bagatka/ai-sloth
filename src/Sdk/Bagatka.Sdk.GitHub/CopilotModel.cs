using System.Collections.Generic;

namespace Bagatka.Sdk.GitHub;

/// <summary>A model a Copilot plan can chat with.</summary>
/// <param name="Id">Its ID, as Copilot's tools take it, such as <c>claude-opus-5.5</c>.</param>
/// <param name="Name">Its name for people, such as <c>Claude Opus 5.5</c>.</param>
/// <param name="UsesTools">Whether it can call tools.</param>
/// <param name="ReasoningEfforts">The reasoning efforts Copilot says it takes, such as <c>low</c>; empty when it names none.</param>
/// <param name="ContextWindow">The most tokens its context holds, when Copilot says.</param>
/// <param name="MaxOutput">The most tokens it writes in one answer, when Copilot says.</param>
/// <param name="Endpoints">The APIs Copilot serves it through, such as <c>/responses</c>; empty when it names none.</param>
public sealed record CopilotModel(string Id, string Name, bool UsesTools, IReadOnlyList<string> ReasoningEfforts, int? ContextWindow, int? MaxOutput, IReadOnlyList<string> Endpoints);
