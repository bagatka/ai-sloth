using System.Collections.Generic;

namespace Bagatka.Sdk.OpenRouter;

/// <summary>A model OpenRouter offers.</summary>
/// <param name="Id">Its ID in calls, such as <c>openai/gpt-6-luna</c>.</param>
/// <param name="Name">Its name for people, such as <c>OpenAI: GPT-6 Luna</c>.</param>
/// <param name="SupportedParameters">The request parameters it takes, such as <c>tools</c>.</param>
public sealed record OpenRouterModel(string Id, string Name, IReadOnlyList<string> SupportedParameters);
