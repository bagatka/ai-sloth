namespace Bagatka.ModelAccess;

/// <summary>
/// What one call of an agent to its model used, as the model's answer reports it, in tokens. Every input
/// token is counted once: <see cref="Input"/>, <see cref="CacheRead"/>, <see cref="CacheWrite"/>, and
/// <see cref="CacheWriteLong"/> add up to the call's whole input.
/// </summary>
/// <param name="Model">The model that answered, as its answer names it, such as <c>gpt-4.1-2025-04-14</c>.</param>
/// <param name="Input">Input tokens neither read from a cache nor written to one.</param>
/// <param name="CacheRead">Input tokens read from a cache.</param>
/// <param name="CacheWrite">Input tokens written to a cache kept minutes (Anthropic's five).</param>
/// <param name="CacheWriteLong">Input tokens written to a cache kept an hour (Anthropic's longer one).</param>
/// <param name="Output">Output tokens, reasoning included.</param>
/// <param name="Reasoning">Of the output, the reasoning tokens.</param>
/// <param name="Cost">What the vendor says the call cost, in US dollars, when it says (OpenRouter does).</param>
public sealed record ModelCallUsage(string Model, int Input, int CacheRead, int CacheWrite, int CacheWriteLong, int Output, int Reasoning, decimal? Cost);
