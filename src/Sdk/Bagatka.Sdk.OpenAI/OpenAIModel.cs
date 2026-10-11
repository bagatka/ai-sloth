using System;
using System.Collections.Generic;

namespace Bagatka.Sdk.OpenAI;

/// <summary>A model an OpenAI API serves.</summary>
/// <param name="Id">Its ID in calls, such as <c>gpt-6.1-sol</c>.</param>
/// <param name="DisplayName">
/// Its name for people, such as <c>GPT-6.1 Sol</c>, in a ChatGPT plan's catalog; the API's own list
/// names models by their IDs alone, so <see langword="null"/> there.
/// </param>
/// <param name="Created">When OpenAI made it available; <see langword="null"/> when the answer doesn't say.</param>
/// <param name="ReasoningEfforts">How hard it can reason, when a catalog says, such as <c>low</c> to <c>xhigh</c>; empty otherwise.</param>
/// <param name="ContextWindow">How many tokens its context holds, when a catalog says.</param>
public sealed record OpenAIModel(string Id, string? DisplayName, DateTimeOffset? Created, IReadOnlyList<string> ReasoningEfforts, int? ContextWindow);
