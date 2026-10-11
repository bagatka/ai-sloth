namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>The prices of calls with more input tokens than <paramref name="AboveInputTokens"/>.</summary>
/// <param name="AboveInputTokens">The threshold, counting every input token, cached or not.</param>
/// <param name="Input">Each input token not read from a cache.</param>
/// <param name="Output">Each output token.</param>
/// <param name="CacheRead">Each input token read from a cache.</param>
/// <param name="CacheWrite">Each input token written to a cache.</param>
public sealed record PriceTier(int AboveInputTokens, decimal Input, decimal Output, decimal? CacheRead, decimal? CacheWrite);
