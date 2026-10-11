using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bagatka.Sdk.OpenAI;

// The shapes of OpenAI's and ChatGPT's answers, as they send them.
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
        [property: JsonPropertyName("visibility")] string? Visibility,
        [property: JsonPropertyName("supported_reasoning_levels")] IReadOnlyList<JsonElement>? SupportedReasoningLevels,
        [property: JsonPropertyName("context_window")] long? ContextWindow);

    internal sealed record IdTokenClaims(
        [property: JsonPropertyName("sub")] string? Subject,
        [property: JsonPropertyName("email")] string? Email,
        [property: JsonPropertyName("https://api.openai.com/auth")] AuthClaims? Auth);

    internal sealed record CodexUserCodeRequest([property: JsonPropertyName("client_id")] string ClientId);

    // Codex reads user_code or usercode, and the interval as a number or a string.
    internal sealed record CodexUserCode(
        [property: JsonPropertyName("device_auth_id")] string? DeviceAuthId,
        [property: JsonPropertyName("user_code")] string? UserCode,
        [property: JsonPropertyName("usercode")] string? UserCodeUnsplit,
        [property: JsonPropertyName("interval")] JsonElement? Interval);

    internal sealed record CodexPollRequest(
        [property: JsonPropertyName("device_auth_id")] string DeviceAuthId,
        [property: JsonPropertyName("user_code")] string UserCode);

    internal sealed record CodexApproval(
        [property: JsonPropertyName("authorization_code")] string? AuthorizationCode,
        [property: JsonPropertyName("code_verifier")] string? CodeVerifier);

    internal sealed record CodexRefreshRequest(
        [property: JsonPropertyName("client_id")] string ClientId,
        [property: JsonPropertyName("grant_type")] string GrantType,
        [property: JsonPropertyName("refresh_token")] string RefreshToken,
        [property: JsonPropertyName("scope")] string Scope);

    internal sealed record CodexTokens(
        [property: JsonPropertyName("id_token")] string? IdToken,
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] long? ExpiresIn);

    internal sealed record AuthClaims([property: JsonPropertyName("chatgpt_account_id")] string? ChatGptAccountId);
}
