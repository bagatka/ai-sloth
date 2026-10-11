using System.Collections.Generic;

namespace Bagatka.Sdk.OpenRouter;

// OpenRouter's JSON, as it sends it.
internal static class OpenRouterWire
{
    internal sealed record ModelList(IReadOnlyList<Model> Data);

    internal sealed record Model(string Id, string? Name, IReadOnlyList<string>? SupportedParameters, long? ContextLength, Pricing? Pricing);

    // Dollars per token, as decimal strings; -1 for a price set per call, as its router's.
    internal sealed record Pricing(string? Prompt, string? Completion, string? InputCacheRead, string? InputCacheWrite);
}
