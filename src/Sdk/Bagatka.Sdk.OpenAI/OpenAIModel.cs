using System;

namespace Bagatka.Sdk.OpenAI;

/// <summary>A model an OpenAI API serves.</summary>
/// <param name="Id">Its ID in calls, such as <c>gpt-6.1-sol</c>.</param>
/// <param name="DisplayName">
/// Its name for people, such as <c>GPT-6.1 Sol</c>, in a ChatGPT plan's catalog; the API's own list
/// names models by their IDs alone, so <see langword="null"/> there.
/// </param>
/// <param name="Created">When OpenAI made it available; <see langword="null"/> when the answer doesn't say.</param>
public sealed record OpenAIModel(string Id, string? DisplayName, DateTimeOffset? Created);
