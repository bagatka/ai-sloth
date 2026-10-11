namespace Bagatka.Sdk.OpenRouter;

/// <summary>What a model's tokens cost at OpenRouter, in US dollars per token.</summary>
/// <param name="Prompt">Each input token.</param>
/// <param name="Completion">Each output token.</param>
/// <param name="CacheRead">Each input token read from a cache, when priced apart.</param>
/// <param name="CacheWrite">Each input token written to a cache, when priced apart.</param>
public sealed record OpenRouterPrice(decimal Prompt, decimal Completion, decimal? CacheRead, decimal? CacheWrite);
