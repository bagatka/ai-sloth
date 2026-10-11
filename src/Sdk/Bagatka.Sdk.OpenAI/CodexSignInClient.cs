using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// Signs in to a ChatGPT plan the way Codex does (<c>codex login --device-auth</c>): a code the person
/// enters at OpenAI, for Codex's own OAuth client, and renewals of its tokens. The tokens call ChatGPT's
/// Codex backend (<c>https://chatgpt.com/backend-api/codex</c>), whose answers carry the plan's limits,
/// and ChatGPT's usage (<see cref="ChatGptUsageClient"/>). Codex's own flow, undocumented. Never
/// retried. An unexpected answer throws <see cref="HttpRequestException"/> with its status code.
/// Thread-safe.
/// </summary>
public sealed class CodexSignInClient : IDisposable
{
    /// <summary>Codex's OAuth client at OpenAI.</summary>
    public const string ClientId = "app_EMoamEEZ73f0CkXaXp7hrann";

    // OpenAI keeps a code for fifteen minutes, as Codex tells the person.
    private static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(5);

    private readonly HttpClient _http;
    private readonly Uri _issuer;
    private readonly TimeProvider _time;

    /// <summary>Creates a client for OpenAI's authorization server at <paramref name="issuer"/>, <c>https://auth.openai.com</c>.</summary>
    public CodexSignInClient(Uri issuer, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        ArgumentNullException.ThrowIfNull(time);
        _issuer = new Uri(issuer.AbsoluteUri.TrimEnd('/') + "/");
        _time = time;
        SocketsHttpHandler handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler, disposeHandler: true) { BaseAddress = _issuer, Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bagatka.Sdk.OpenAI", "1.0"));
    }

    /// <summary>A code for the person to enter at the answer's URL.</summary>
    public async Task<CodexDeviceCode> StartAsync(CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync(
            new Uri("api/accounts/deviceauth/usercode", UriKind.Relative), new OpenAIWire.CodexUserCodeRequest(ClientId), OpenAIJsonContext.Default.CodexUserCodeRequest, ct).ConfigureAwait(false);
        OpenAIWire.CodexUserCode answer = await ReadAsync(response, OpenAIJsonContext.Default.CodexUserCode, "POST /api/accounts/deviceauth/usercode", ct).ConfigureAwait(false);
        string? userCode = answer.UserCode ?? answer.UserCodeUnsplit;
        if (answer.DeviceAuthId is null || userCode is null)
        {
            throw new HttpRequestException("OpenAI's code has no device or user code.");
        }

        bool timed = int.TryParse(answer.Interval?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seconds);
        TimeSpan interval = timed && seconds > 0 ? TimeSpan.FromSeconds(seconds) : DefaultInterval;
        return new CodexDeviceCode(answer.DeviceAuthId, userCode, new Uri(_issuer, "codex/device"), CodeLifetime, interval);
    }

    /// <summary>
    /// The plan's tokens once the person entered and approved the code; <see langword="null"/> while
    /// they haven't. A code works once.
    /// </summary>
    public async Task<ChatGptTokens?> PollAsync(string deviceAuthId, string userCode, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync(
            new Uri("api/accounts/deviceauth/token", UriKind.Relative), new OpenAIWire.CodexPollRequest(deviceAuthId, userCode), OpenAIJsonContext.Default.CodexPollRequest, ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            return null;
        }

        OpenAIWire.CodexApproval approval = await ReadAsync(response, OpenAIJsonContext.Default.CodexApproval, "POST /api/accounts/deviceauth/token", ct).ConfigureAwait(false);
        if (approval.AuthorizationCode is null || approval.CodeVerifier is null)
        {
            throw new HttpRequestException("OpenAI approved the code without an authorization code.");
        }

        Dictionary<string, string> form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = approval.AuthorizationCode,
            ["redirect_uri"] = new Uri(_issuer, "deviceauth/callback").AbsoluteUri,
            ["client_id"] = ClientId,
            ["code_verifier"] = approval.CodeVerifier,
        };
        using FormUrlEncodedContent content = new FormUrlEncodedContent(form);
        using HttpResponseMessage exchanged = await _http.PostAsync(new Uri("oauth/token", UriKind.Relative), content, ct).ConfigureAwait(false);
        OpenAIWire.CodexTokens tokens = await ReadAsync(exchanged, OpenAIJsonContext.Default.CodexTokens, "POST /oauth/token", ct).ConfigureAwait(false);
        ChatGptTokens? signedIn = TokensOf(tokens, refreshToken: null);
        if (signedIn is null)
        {
            throw new HttpRequestException("OpenAI's tokens lack an access or refresh token.");
        }

        return signedIn;
    }

    /// <summary>
    /// The next token set. The refresh token it replaces stops working, so refreshes of one sign-in must
    /// never run at the same time. Fails with <c>openai.&lt;error&gt;</c> when OpenAI refuses: the
    /// sign-in has ended.
    /// </summary>
    public async Task<Result<ChatGptTokens>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        using HttpResponseMessage response = await _http.PostAsJsonAsync(
            new Uri("oauth/token", UriKind.Relative), new OpenAIWire.CodexRefreshRequest(ClientId, "refresh_token", refreshToken, "openid profile email"), OpenAIJsonContext.Default.CodexRefreshRequest, ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            string code = body.Contains("refresh_token_reused", StringComparison.Ordinal) ? "refresh_token_reused" : "invalid_grant";
            return new Result<ChatGptTokens>(Error.Conflict("openai." + code, "OpenAI refused to renew Codex's sign-in."));
        }

        OpenAIWire.CodexTokens tokens = await ReadAsync(response, OpenAIJsonContext.Default.CodexTokens, "POST /oauth/token", ct).ConfigureAwait(false);
        ChatGptTokens? renewed = TokensOf(tokens, refreshToken);
        if (renewed is null)
        {
            throw new HttpRequestException("OpenAI's renewal lacks an access token.");
        }

        return new Result<ChatGptTokens>(renewed);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }

    // Codex's tokens say when they expire in the access token itself; a renewal may keep the refresh token.
    private ChatGptTokens? TokensOf(OpenAIWire.CodexTokens tokens, string? refreshToken)
    {
        string? refresh = tokens.RefreshToken ?? refreshToken;
        if (tokens.AccessToken is null || refresh is null)
        {
            return null;
        }

        DateTimeOffset now = _time.GetUtcNow();
        DateTimeOffset expiresAt = ExpiryOf(tokens.AccessToken) ?? now + TimeSpan.FromSeconds(tokens.ExpiresIn ?? 3600);
        return new ChatGptTokens(tokens.AccessToken, refresh, tokens.IdToken, expiresAt - now);
    }

    private static DateTimeOffset? ExpiryOf(string accessToken)
    {
        string[] parts = accessToken.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            using JsonDocument payload = JsonDocument.Parse(System.Buffers.Text.Base64Url.DecodeFromChars(parts[1]));
            bool present = payload.RootElement.TryGetProperty("exp", out JsonElement expiry);
            return present && expiry.ValueKind == JsonValueKind.Number ? DateTimeOffset.FromUnixTimeSeconds(expiry.GetInt64()) : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type, string request, CancellationToken ct)
        where T : class
    {
        if (!response.IsSuccessStatusCode)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"OpenAI answered {(int)response.StatusCode} to {request}.");
            throw new HttpRequestException(message, inner: null, response.StatusCode);
        }

        T? value;
        try
        {
            value = await response.Content.ReadFromJsonAsync(type, ct).ConfigureAwait(false);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("OpenAI's answer to " + request + " isn't the JSON expected.", exception);
        }

        if (value is null)
        {
            throw new HttpRequestException("OpenAI answered " + request + " with nothing.");
        }

        return value;
    }
}
