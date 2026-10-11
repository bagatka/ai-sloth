using System.Collections.Generic;

namespace Bagatka.Sdk.Anthropic;

// Anthropic's JSON, as it sends it.
internal static class AnthropicWire
{
    internal sealed record ModelList(IReadOnlyList<Model> Data);

    internal sealed record Model(string Id, string? DisplayName);
}
