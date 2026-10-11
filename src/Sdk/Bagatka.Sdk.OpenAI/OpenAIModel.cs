using System;

namespace Bagatka.Sdk.OpenAI;

/// <summary>A model an OpenAI API serves.</summary>
/// <param name="Id">Its ID in calls, such as <c>gpt-6.1-sol</c>.</param>
/// <param name="Created">When OpenAI made it available; <see langword="null"/> when the API doesn't say.</param>
public sealed record OpenAIModel(string Id, DateTimeOffset? Created);
