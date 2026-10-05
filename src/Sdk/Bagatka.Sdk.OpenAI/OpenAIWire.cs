using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bagatka.Sdk.OpenAI;

// The shapes of OpenAI's authorization server responses, as it sends them.
internal static class OpenAIWire
{
    internal sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("scope")] string? Scope,
        [property: JsonPropertyName("expires_in")] long? ExpiresIn,
        [property: JsonPropertyName("earliest_refresh_at")] long? EarliestRefreshAt);

    internal sealed record ErrorResponse(
        [property: JsonPropertyName("error")] string? Error,
        [property: JsonPropertyName("error_description")] string? Description);

    // The audience is a string or an array of them.
    internal sealed record IdTokenClaims(
        [property: JsonPropertyName("iss")] string? Issuer,
        [property: JsonPropertyName("sub")] string? Subject,
        [property: JsonPropertyName("aud")] JsonElement Audience,
        [property: JsonPropertyName("exp")] long? ExpiresAt,
        [property: JsonPropertyName("nonce")] string? Nonce,
        [property: JsonPropertyName("email")] string? Email);
}
