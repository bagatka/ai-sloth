using System.Collections.Generic;
using System.Linq;

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
public sealed record ModelPrice(decimal Input, decimal Output, decimal? CacheRead, decimal? CacheWrite, IReadOnlyList<PriceTier> Tiers, bool Estimate)
{
    /// <summary>
    /// What a call costs in US dollars, at the prices of the highest tier its whole input passes.
    /// Tokens read from or written to a cache cost their own prices, when the vendor sets them, and the
    /// input price otherwise; tokens written to a cache kept an hour, Anthropic's, cost twice the input
    /// price.
    /// </summary>
    /// <param name="input">Input tokens neither read from a cache nor written to one.</param>
    /// <param name="cacheRead">Input tokens read from a cache.</param>
    /// <param name="cacheWrite">Input tokens written to a cache kept minutes.</param>
    /// <param name="cacheWriteLong">Input tokens written to a cache kept an hour.</param>
    /// <param name="output">Output tokens, reasoning included.</param>
    public decimal CostOf(long input, long cacheRead, long cacheWrite, long cacheWriteLong, long output)
    {
        long whole = input + cacheRead + cacheWrite + cacheWriteLong;
        PriceTier? tier = Tiers.LastOrDefault(found => whole > found.AboveInputTokens);
        decimal inputPrice = tier?.Input ?? Input;
        decimal readPrice = tier?.CacheRead ?? CacheRead ?? inputPrice;
        decimal writePrice = tier?.CacheWrite ?? CacheWrite ?? inputPrice;
        decimal outputPrice = tier?.Output ?? Output;
        decimal millionths = (input * inputPrice) + (cacheRead * readPrice) + (cacheWrite * writePrice) + (cacheWriteLong * 2 * inputPrice) + (output * outputPrice);
        return millionths / 1_000_000m;
    }
}
