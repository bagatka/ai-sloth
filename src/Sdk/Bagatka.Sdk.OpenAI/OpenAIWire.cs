using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bagatka.Sdk.OpenAI;

// The shapes of OpenAI's authorization server responses, as it sends them.
internal static class OpenAIWire
{
    // The API's list of models, or, for a ChatGPT plan's token, the plan's catalog.
    internal sealed record ModelList(
        [property: JsonPropertyName("data")] IReadOnlyList<Model>? Data,
        [property: JsonPropertyName("models")] IReadOnlyList<CatalogModel>? Models);

    internal sealed record Model([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("created")] long? Created);

    internal sealed record CatalogModel(
        [property: JsonPropertyName("slug")] string Slug,
        [property: JsonPropertyName("display_name")] string? DisplayName,
        [property: JsonPropertyName("visibility")] string? Visibility);

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
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("https://api.openai.com/auth")] AuthClaims? Auth);

    internal sealed record AuthClaims([property: JsonPropertyName("chatgpt_account_id")] string? ChatGptAccountId);
}
