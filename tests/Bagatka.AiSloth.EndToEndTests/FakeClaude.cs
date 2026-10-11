using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Claude's sign-in for Claude Code's own OAuth client at the HTTP boundary, strict where Anthropic's
/// is: the person approves at the authorize page, which shows them the code and state to bring back;
/// the code works once, with its PKCE verifier and state; every renewal replaces the refresh token and
/// refuses one used twice. Access tokens last a minute, so every use renews them. Calls with them go to
/// the fake model's Anthropic API, which reads their expiry from the token (<see cref="Unexpired"/>).
/// </summary>
internal sealed class FakeClaude : IAsyncDisposable
{
    /// <summary>Claude Code's OAuth client at Anthropic.</summary>
    public const string ClientId = "9d1c250a-e61b-44d9-88ed-5944d1962f5e";

    /// <summary>The beta Anthropic's API wants with a plan's token.</summary>
    public const string OAuthBeta = "oauth-2025-04-20";

    private const string RedirectUri = "https://platform.claude.com/oauth/code/callback";

    // An access token: this prefix, when it expires in Unix seconds, and a random part.
    private const string TokenPrefix = "sk-ant-oat01-e2e-";

    private readonly WebApplication _app;
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<string, PendingCode> _codes = new Dictionary<string, PendingCode>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _refreshTokens = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly HashSet<string> _usedRefreshTokens = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> _signedOut = new HashSet<string>(StringComparer.Ordinal);

    private FakeClaude(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Where it runs, such as <c>http://127.0.0.1:41237/</c>.</summary>
    public Uri Url { get; private set; } = new Uri("http://localhost");

    /// <summary>Claude's page where a person approves Claude Code's sign-in.</summary>
    public Uri AuthorizeUrl => new Uri(Url, "oauth/authorize");

    /// <summary>Where a code becomes tokens, and tokens are renewed.</summary>
    public Uri TokenUrl => new Uri(Url, "v1/oauth/token");

    /// <summary>How many renewals it answered with new tokens.</summary>
    public int Renewals { get; private set; }

    /// <summary>How many renewals came with a refresh token already used: renewals that raced.</summary>
    public int ReusedRefreshTokens { get; private set; }

    public static async Task<FakeClaude> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        WebApplication app = builder.Build();
        FakeClaude claude = new FakeClaude(app);
        app.MapGet("/oauth/authorize", claude.Authorize);
        app.MapPost("/v1/oauth/token", claude.TokenAsync);
        await app.StartAsync();
        IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        if (addresses is null)
        {
            throw new InvalidOperationException("Kestrel reported no addresses.");
        }

        claude.Url = new Uri(addresses.Addresses.Single());
        return claude;
    }

    /// <summary>
    /// The person signed in to Claude as <paramref name="person"/> opens the sign-in URL and approves;
    /// returns the code Claude's page shows them.
    /// </summary>
    public static async Task<string> ApproveAsync(Uri signInUrl, string person)
    {
        using HttpClient browser = new HttpClient();
        Uri approved = new Uri(signInUrl.AbsoluteUri + "&e2e_person=" + Uri.EscapeDataString(person));
        using HttpResponseMessage response = await browser.GetAsync(approved, TestContext.Current.CancellationToken);
        string page = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Claude refused the sign-in: {(int)response.StatusCode} {page}"));
        }

        return page;
    }

    /// <summary>Whether an access token it issued is still good: calls with an expired one are refused.</summary>
    public static bool Unexpired(string accessToken)
    {
        string[] parts = accessToken.StartsWith(TokenPrefix, StringComparison.Ordinal) ? accessToken[TokenPrefix.Length..].Split('-') : [];
        bool dated = long.TryParse(parts.FirstOrDefault(), NumberStyles.None, CultureInfo.InvariantCulture, out long expiresAt);
        return dated && parts.Length == 2 && expiresAt > TimeProvider.System.GetUtcNow().ToUnixTimeSeconds();
    }

    /// <summary>The person signs out of Claude Code everywhere: their refresh tokens stop working.</summary>
    public void SignOut(string person)
    {
        lock (_lock)
        {
            _signedOut.Add(person);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    // Approving shows the code and the state, as Claude's callback page does.
    private IResult Authorize(HttpRequest request)
    {
        IQueryCollection query = request.Query;
        bool valid = string.Equals(query["code"], "true", StringComparison.Ordinal)
            && string.Equals(query["client_id"], ClientId, StringComparison.Ordinal)
            && string.Equals(query["response_type"], "code", StringComparison.Ordinal)
            && string.Equals(query["redirect_uri"], RedirectUri, StringComparison.Ordinal)
            && query["scope"].ToString().Split(' ').Contains("user:inference", StringComparer.Ordinal)
            && string.Equals(query["code_challenge_method"], "S256", StringComparison.Ordinal)
            && query["state"].ToString().Length > 0
            && query["e2e_person"].ToString().Length > 0;
        if (!valid)
        {
            return Results.BadRequest("Invalid authorization request.");
        }

        string code = "claude-code-" + RandomNumberGenerator.GetHexString(16, lowercase: true);
        lock (_lock)
        {
            _codes[code] = new PendingCode(query["e2e_person"].ToString(), query["code_challenge"].ToString(), query["state"].ToString());
        }

        return Results.Text(code + "#" + query["state"]);
    }

    private async Task<IResult> TokenAsync(HttpRequest request)
    {
        JsonNode? body = await JsonNode.ParseAsync(request.Body, cancellationToken: request.HttpContext.RequestAborted);
        string grant = (string?)body?["grant_type"] ?? string.Empty;
        bool ours = string.Equals((string?)body?["client_id"], ClientId, StringComparison.Ordinal);
        lock (_lock)
        {
            if (ours && string.Equals(grant, "authorization_code", StringComparison.Ordinal))
            {
                return Exchange(body!);
            }

            if (ours && string.Equals(grant, "refresh_token", StringComparison.Ordinal))
            {
                return Refresh(body!);
            }
        }

        return Refused("invalid_request");
    }

    // Callers hold the lock.
    private IResult Exchange(JsonNode body)
    {
        string code = (string?)body["code"] ?? string.Empty;
        bool known = _codes.Remove(code, out PendingCode? pending);
        string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes((string?)body["code_verifier"] ?? string.Empty)));
        bool genuine = known
            && string.Equals((string?)body["redirect_uri"], RedirectUri, StringComparison.Ordinal)
            && string.Equals((string?)body["state"], pending!.State, StringComparison.Ordinal)
            && string.Equals(challenge, pending.Challenge, StringComparison.Ordinal);
        return genuine ? Tokens(pending!.Person) : Refused("invalid_grant");
    }

    // Callers hold the lock.
    private IResult Refresh(JsonNode body)
    {
        string refreshToken = (string?)body["refresh_token"] ?? string.Empty;
        if (_usedRefreshTokens.Contains(refreshToken))
        {
            ReusedRefreshTokens++;
            return Refused("invalid_grant");
        }

        bool issued = _refreshTokens.Remove(refreshToken, out string? person);
        if (!issued || _signedOut.Contains(person!) || ((string?)body["scope"])?.Length is null or 0)
        {
            return Refused("invalid_grant");
        }

        _usedRefreshTokens.Add(refreshToken);
        Renewals++;
        return Tokens(person!);
    }

    // Callers hold the lock.
    private IResult Tokens(string person)
    {
        long expiresAt = TimeProvider.System.GetUtcNow().ToUnixTimeSeconds() + 60;
        string access = TokenPrefix + expiresAt.ToString(CultureInfo.InvariantCulture) + "-" + RandomNumberGenerator.GetHexString(16, lowercase: true);
        string refresh = "sk-ant-ort01-e2e-" + RandomNumberGenerator.GetHexString(24, lowercase: true);
        _refreshTokens[refresh] = person;
        JsonObject answer = new JsonObject
        {
            ["token_type"] = "Bearer",
            ["access_token"] = access,
            ["refresh_token"] = refresh,
            ["expires_in"] = 60,
            ["scope"] = "user:inference user:profile",
            ["account"] = new JsonObject { ["uuid"] = "account-" + person, ["email_address"] = person + "@example.com" },
        };
        return Results.Text(answer.ToJsonString(), "application/json");
    }

    private static IResult Refused(string error)
    {
        return Results.Json(new JsonObject { ["error"] = error, ["error_description"] = "Refused by the fake: " + error }, statusCode: StatusCodes.Status400BadRequest);
    }

    private sealed record PendingCode(string Person, string Challenge, string State);
}
