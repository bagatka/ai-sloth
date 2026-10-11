using System.Collections.Generic;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// What a model's tokens cost, in US dollars per million: what an API key pays its vendor, or, for a
/// plan, which pays no price per token, what the same tokens would cost at the vendor's API.
/// </summary>
/// <param name="Input">Each input token not read from a cache.</param>
/// <param name="Output">Each output token, reasoning included.</param>
/// <param name="CacheRead">Each input token read from a cache, when the vendor prices it apart.</param>
/// <param name="CacheWrite">Each input token written to a cache, when the vendor prices it apart.</param>
/// <param name="Tiers">Higher prices for calls with more input than each tier's threshold, lowest first.</param>
/// <param name="Estimate">Whether it's a plan's estimate at the vendor's API prices rather than a price paid.</param>
public sealed record ModelPrice(decimal Input, decimal Output, decimal? CacheRead, decimal? CacheWrite, IReadOnlyList<PriceTier> Tiers, bool Estimate);
