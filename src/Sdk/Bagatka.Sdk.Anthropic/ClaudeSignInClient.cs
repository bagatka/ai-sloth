using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.Sdk.Anthropic;

/// <summary>
/// Signs in to a Claude plan the way Claude Code does with a Claude account, for Claude Code's own
/// OAuth client: the person approves at Claude's page (<see cref="AuthorizationUrl"/>), which then
/// shows them a code to bring back (<see cref="ExchangeAsync"/>); then renews its tokens
/// (<see cref="RefreshAsync"/>). The tokens call Anthropic's API for Claude Code on the plan, and read
/// its usage (<see cref="ClaudePlanUsageClient"/>). Claude Code's own flow, undocumented. Never
/// retried. An unexpected answer throws <see cref="HttpRequestException"/> with its status code.
/// Thread-safe.
/// </summary>
public sealed class ClaudeSignInClient : IDisposable
{
    /// <summary>Claude Code's OAuth client at Anthropic.</summary>
    public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    // The scopes Claude Code asks for, so the sign-in is its own.
    private const string Scopes = "user:profile user:inference user:sessions:claude_code user:mcp_servers user:file_upload";

    private readonly HttpClient _http;
    private readonly ClaudeSignInSettings _settings;

    /// <summary>Creates a client for the sign-in at <paramref name="settings"/>.</summary>
    public ClaudeSignInClient(ClaudeSignInSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        SocketsHttpHandler handler = new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) };
        _http = new HttpClient(handler, disposeHandler: true) { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Bagatka.Sdk.Anthropic", "1.0"));
    }

    /// <summary>The PKCE challenge for a verifier: its SHA-256 digest, base64url-encoded without padding.</summary>
    public static string CodeChallenge(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(codeVerifier);
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
    }

    /// <summary>Where the person approves; Claude then shows them a code, with <paramref name="state"/> after a <c>#</c>.</summary>
    public Uri AuthorizationUrl(string state, string codeChallenge)
    {
        List<KeyValuePair<string, string>> query =
        [
            new KeyValuePair<string, string>("code", "true"),
            new KeyValuePair<string, string>("client_id", ClientId),
            new KeyValuePair<string, string>("response_type", "code"),
            new KeyValuePair<string, string>("redirect_uri", _settings.RedirectUri.AbsoluteUri),
            new KeyValuePair<string, string>("scope", Scopes),
            new KeyValuePair<string, string>("code_challenge", codeChallenge),
            new KeyValuePair<string, string>("code_challenge_method", "S256"),
            new KeyValuePair<string, string>("state", state),
        ];
        StringBuilder url = new StringBuilder(_settings.AuthorizeUrl.AbsoluteUri);
        char separator = '?';
        foreach (KeyValuePair<string, string> parameter in query)
        {
            url.Append(separator).Append(Uri.EscapeDataString(parameter.Key)).Append('=').Append(Uri.EscapeDataString(parameter.Value));
            separator = '&';
        }

        return new Uri(url.ToString());
    }

    /// <summary>
    /// Exchanges the code Claude showed for the plan's tokens. Fails with <c>anthropic.&lt;error&gt;</c>
    /// when Anthropic refuses it, such as for a used or expired code.
    /// </summary>
    public async Task<Result<ClaudeTokens>> ExchangeAsync(string code, string state, string codeVerifier, CancellationToken ct)
    {
        AnthropicWire.CodeExchange body = new AnthropicWire.CodeExchange("authorization_code", code, _settings.RedirectUri.AbsoluteUri, ClientId, codeVerifier, state);
        using HttpResponseMessage response = await _http.PostAsJsonAsync(_settings.TokenUrl, body, AnthropicJsonContext.Default.CodeExchange, ct).ConfigureAwait(false);
        return await TokensAsync(response, refreshToken: null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// The next tokens. The refresh token they replace may stop working, so renewals of one sign-in
    /// must never run at the same time. Fails with <c>anthropic.&lt;error&gt;</c> when Anthropic
    /// refuses: the sign-in has ended.
    /// </summary>
    public async Task<Result<ClaudeTokens>> RefreshAsync(string refreshToken, CancellationToken ct)
    {
        AnthropicWire.Refresh body = new AnthropicWire.Refresh("refresh_token", refreshToken, ClientId, Scopes);
        using HttpResponseMessage response = await _http.PostAsJsonAsync(_settings.TokenUrl, body, AnthropicJsonContext.Default.Refresh, ct).ConfigureAwait(false);
        return await TokensAsync(response, refreshToken, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _http.Dispose();
    }

    private static async Task<Result<ClaudeTokens>> TokensAsync(HttpResponseMessage response, string? refreshToken, CancellationToken ct)
    {
        string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
        {
            string error = ErrorOf(body) ?? "invalid_grant";
            return new Result<ClaudeTokens>(Error.Conflict("anthropic." + error, "Anthropic refused Claude Code's sign-in (" + error + ")."));
        }

        if (!response.IsSuccessStatusCode)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Anthropic's token endpoint answered {(int)response.StatusCode}.");
            throw new HttpRequestException(message, inner: null, response.StatusCode);
        }

        AnthropicWire.TokenAnswer? answer;
        try
        {
            answer = JsonSerializer.Deserialize(body, AnthropicJsonContext.Default.TokenAnswer);
        }
        catch (JsonException exception)
        {
            throw new HttpRequestException("Anthropic's tokens aren't the JSON expected.", exception);
        }

        string? refresh = answer?.RefreshToken ?? refreshToken;
        if (answer?.AccessToken is null || refresh is null)
        {
            throw new HttpRequestException("Anthropic's answer lacks an access or refresh token.");
        }

        TimeSpan expiresIn = TimeSpan.FromSeconds(answer.ExpiresIn ?? 28_800);
        return new Result<ClaudeTokens>(new ClaudeTokens(answer.AccessToken, refresh, expiresIn, answer.Account?.Uuid, answer.Account?.EmailAddress));
    }

    private static string? ErrorOf(string body)
    {
        try
        {
            return JsonSerializer.Deserialize(body, AnthropicJsonContext.Default.ErrorAnswer)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
