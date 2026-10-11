using System.Collections.Generic;

namespace Bagatka.ModelAccess.Catalog;

// models.dev's catalog as its api.json writes it (snake_case), only what the catalog reads: a model's
// reasoning, limits, and prices. Its prices are US dollars per million tokens.
internal static class ModelsDev
{
    internal sealed record Model(
        string Id,
        string? Name,
        bool Reasoning,
        IReadOnlyList<ReasoningOption>? ReasoningOptions,
        bool ToolCall,
        Limit? Limit,
        Cost? Cost);

    // "effort" with the values the model takes, "budget_tokens", or "toggle".
    internal sealed record ReasoningOption(string Type, IReadOnlyList<string>? Values);

    internal sealed record Limit(long Context, long Output);

    internal sealed record Cost(
        decimal Input,
        decimal Output,
        decimal? CacheRead,
        decimal? CacheWrite,
        IReadOnlyList<CostTier>? Tiers,
        Prices? ContextOver200k);

    internal sealed record CostTier(Tier Tier, decimal Input, decimal Output, decimal? CacheRead, decimal? CacheWrite);

    // "context" tiers apply above Size input tokens.
    internal sealed record Tier(string Type, long Size);

    internal sealed record Prices(decimal Input, decimal Output, decimal? CacheRead, decimal? CacheWrite);
}
