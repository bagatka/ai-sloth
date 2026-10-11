using System.Collections.Generic;

namespace Bagatka.Sdk.OpenRouter;

/// <summary>A model OpenRouter offers.</summary>
/// <param name="Id">Its ID in calls, such as <c>openai/gpt-6-luna</c>.</param>
/// <param name="Name">Its name for people, such as <c>OpenAI: GPT-6 Luna</c>.</param>
/// <param name="SupportedParameters">The request parameters it takes, such as <c>tools</c> and <c>reasoning</c>.</param>
/// <param name="ContextLength">The most tokens its context holds, when OpenRouter says.</param>
/// <param name="Price">What its tokens cost, when OpenRouter prices it ahead (its router doesn't).</param>
public sealed record OpenRouterModel(string Id, string Name, IReadOnlyList<string> SupportedParameters, int? ContextLength, OpenRouterPrice? Price);
