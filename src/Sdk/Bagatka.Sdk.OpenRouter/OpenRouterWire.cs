using System.Collections.Generic;

namespace Bagatka.Sdk.OpenRouter;

// OpenRouter's JSON, as it sends it.
internal static class OpenRouterWire
{
    internal sealed record ModelList(IReadOnlyList<Model> Data);

    internal sealed record Model(string Id, string? Name, IReadOnlyList<string>? SupportedParameters);
}
