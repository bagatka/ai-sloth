using System.Collections.Generic;

namespace Bagatka.Sdk.Anthropic;

// Anthropic's JSON, as it sends it.
internal static class AnthropicWire
{
    internal sealed record ModelList(IReadOnlyList<Model> Data);

    internal sealed record Model(string Id, string? DisplayName);

    // Claude Code's sign-in sends JSON, its state with the code.
    internal sealed record CodeExchange(string GrantType, string Code, string RedirectUri, string ClientId, string CodeVerifier, string State);

    internal sealed record Refresh(string GrantType, string RefreshToken, string ClientId, string Scope);

    internal sealed record TokenAnswer(string? AccessToken, string? RefreshToken, long? ExpiresIn, Account? Account);

    internal sealed record Account(string? Uuid, string? EmailAddress);

    internal sealed record ErrorAnswer(string? Error);
}
