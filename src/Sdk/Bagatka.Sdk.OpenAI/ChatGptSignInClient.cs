using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// Sign in with ChatGPT for open-source and self-hosted apps
/// (https://developers.openai.com/siwc/token-sharing-open-source): an OAuth authorization-code flow
/// with PKCE and no client secret, whose tokens call the OpenAI API on the person's ChatGPT plan.
/// Every call changes state at OpenAI, so none is retried. Thread-safe.
/// </summary>
public sealed class ChatGptSignInClient : IDisposable
{
    /// <summary>The client ID a first sign-in registers with; OpenAI answers with the client ID it issued.</summary>
    public const string RegistrationClientId = "dynamic_agent_client";

    /// <summary>The scope that lets the app use the person's plan; sign-in can succeed without it.</summary>
    public const string PlanScope = "chatgpt.tokens.use.direct";

    private const string Scopes = "openid profile email offline_access resource.invoke " + PlanScope;
    private const string CallbackPath = "/auth/callback";

    private readonly HttpClient _http;

    /// <summary>Creates a client for the authorization server in the settings.</summary>
    public ChatGptSignInClient(ChatGptSignInSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        SocketsHttpHandler handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler, disposeHandler: true) { BaseAddress = settings.Authority, Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>What the tokens are for, and where they are sent: the OpenAI API.</summary>
    public static Uri Resource { get; } = new Uri("https://api.openai.com/v1");

    /// <summary>
    /// Whether ChatGPT can return to <paramref name="redirectUri"/>: an HTTP loopback callback on
    /// <c>127.0.0.1</c> with the path <c>/auth/callback</c>, on any port.
    /// </summary>
    public static bool AcceptsRedirect(Uri redirectUri)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        return redirectUri.IsAbsoluteUri
            && string.Equals(redirectUri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal)
            && string.Equals(redirectUri.Host, "127.0.0.1", StringComparison.Ordinal)
            && string.Equals(redirectUri.AbsolutePath, CallbackPath, StringComparison.Ordinal)
            && redirectUri.Query.Length == 0
            && redirectUri.Fragment.Length == 0;
    }

    /// <summary>The PKCE challenge for a verifier: its SHA-256 digest, base64url-encoded without padding.</summary>
    public static string CodeChallenge(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(codeVerifier);
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
    }

    /// <summary>
    /// The claims of an ID token from the token endpoint, or <see langword="null"/> when it isn't one.
    /// The signature isn't checked: the token came straight from the token endpoint over TLS, which
    /// OpenID Connect accepts in place of it. Callers check the issuer, audience, expiry, and nonce.
    /// </summary>
    public static ChatGptIdToken? ReadIdToken(string idToken)
    {
        ArgumentNullException.ThrowIfNull(idToken);
        string[] parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        OpenAIWire.IdTokenClaims? claims;
        try
        {
            byte[] payload = Base64Url.DecodeFromChars(parts[1]);
            claims = JsonSerializer.Deserialize(payload, OpenAIJsonContext.Default.IdTokenClaims);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }

        if (claims?.Issuer is null || claims.Subject is null || claims.ExpiresAt is null)
        {
            return null;
        }

        List<string> audiences = [];
        if (claims.Audience.ValueKind == JsonValueKind.String)
        {
            audiences.Add(claims.Audience.GetString()!);
        }
        else if (claims.Audience.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement audience in claims.Audience.EnumerateArray())
            {
                audiences.Add(audience.GetString() ?? string.Empty);
            }
        }

        return new ChatGptIdToken(claims.Issuer, claims.Subject, audiences, DateTimeOffset.FromUnixTimeSeconds(claims.ExpiresAt.Value), claims.Nonce, claims.Email);
    }

    /// <summary>Where to send the person's browser to sign in; it returns to the request's redirect URI.</summary>
    public Uri AuthorizationUrl(ChatGptAuthorization request)
    {
        ArgumentNullException.ThrowIfNull(request);
        List<KeyValuePair<string, string>> query =
        [
            new KeyValuePair<string, string>("client_id", request.ClientId),
            new KeyValuePair<string, string>("ext_agent_host_id", request.HostId),
            new KeyValuePair<string, string>("response_type", "code"),
            new KeyValuePair<string, string>("redirect_uri", request.RedirectUri.AbsoluteUri),
            new KeyValuePair<string, string>("scope", Scopes),
            new KeyValuePair<string, string>("resource", Resource.AbsoluteUri),
            new KeyValuePair<string, string>("state", request.State),
            new KeyValuePair<string, string>("nonce", request.Nonce),
            new KeyValuePair<string, string>("code_challenge_method", "S256"),
            new KeyValuePair<string, string>("code_challenge", request.CodeChallenge),
        ];
        if (request.AgentNameHint is not null)
        {
            query.Add(new KeyValuePair<string, string>("agent_name_hint", request.AgentNameHint));
        }

        StringBuilder url = new StringBuilder(new Uri(_http.BaseAddress!, "api/accounts/authorize").AbsoluteUri);
        char separator = '?';
        foreach (KeyValuePair<string, string> parameter in query)
        {
            url.Append(separator).Append(Uri.EscapeDataString(parameter.Key)).Append('=').Append(Uri.EscapeDataString(parameter.Value));
            separator = '&';
        }

        return new Uri(url.ToString());
    }

    /// <summary>
    /// Exchanges the code the callback brought for tokens. Fails with <c>openai.&lt;error&gt;</c> when
    /// OpenAI refuses it, such as <c>openai.invalid_grant</c> for a used or expired code.
    /// </summary>
    public async Task<Result<ChatGptTokens>> ExchangeCodeAsync(string clientId, string code, string codeVerifier, Uri redirectUri, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(redirectUri);
        Dictionary<string, string> form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["client_id"] = clientId,
            ["code"] = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"] = redirectUri.AbsoluteUri,
            ["resource"] = Resource.AbsoluteUri,
        };
        return await RequestTokensAsync(form, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The next token set. The refresh token it replaces stops working, so refreshes of one token set
    /// must never run at the same time. Fails with <c>openai.&lt;error&gt;</c> when OpenAI refuses, such
    /// as <c>openai.invalid_grant</c> once the person disconnected the app: the sign-in has ended.
    /// </summary>
    public async Task<Result<ChatGptTokens>> RefreshAsync(string clientId, string refreshToken, CancellationToken ct)
    {
        Dictionary<string, string> form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = clientId,
            ["refresh_token"] = refreshToken,
            ["resource"] = Resource.AbsoluteUri,
        };
        return await RequestTokensAsync(form, ct).ConfigureAwait(false);
    }

    /// <summary>Ends the renewable session, as signing out does. Revoking a token that no longer works succeeds too.</summary>
    public async Task RevokeAsync(string clientId, string refreshToken, CancellationToken ct)
    {
        Dictionary<string, string> form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["token"] = refreshToken,
            ["token_type_hint"] = "refresh_token",
            ["client_id"] = clientId,
        };
        using FormUrlEncodedContent content = new FormUrlEncodedContent(form);
        using HttpResponseMessage response = await _http.PostAsync(new Uri("api/accounts/oauth/revoke", UriKind.Relative), content, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }

    private async Task<Result<ChatGptTokens>> RequestTokensAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using FormUrlEncodedContent content = new FormUrlEncodedContent(form);
        using HttpResponseMessage response = await _http.PostAsync(new Uri("api/accounts/oauth/token", UriKind.Relative), content, ct).ConfigureAwait(false);
        byte[] body = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        bool refused = response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized;
        if (refused)
        {
            OpenAIWire.ErrorResponse? error = Parse(body, OpenAIJsonContext.Default.ErrorResponse);
            if (error?.Error is null)
            {
                throw new HttpRequestException("OpenAI refused a token request without an OAuth error.", inner: null, response.StatusCode);
            }

            return new Result<ChatGptTokens>(Error.Conflict("openai." + error.Error, error.Description ?? error.Error));
        }

        if (!response.IsSuccessStatusCode)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"OpenAI's token endpoint answered {(int)response.StatusCode}.");
            throw new HttpRequestException(message, inner: null, response.StatusCode);
        }

        OpenAIWire.TokenResponse? tokens = Parse(body, OpenAIJsonContext.Default.TokenResponse);
        if (tokens?.AccessToken is null || tokens.RefreshToken is null || tokens.ExpiresIn is null)
        {
            throw new HttpRequestException("OpenAI's token response lacks an access token, a refresh token, or an expiry.");
        }

        DateTimeOffset? earliestRefreshAt = tokens.EarliestRefreshAt is null ? null : DateTimeOffset.FromUnixTimeSeconds(tokens.EarliestRefreshAt.Value);
        return new Result<ChatGptTokens>(new ChatGptTokens(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.IdToken,
            tokens.Scope ?? string.Empty,
            TimeSpan.FromSeconds(tokens.ExpiresIn.Value),
            earliestRefreshAt));
    }

    private static T? Parse<T>(byte[] body, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            return JsonSerializer.Deserialize(body, type);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
