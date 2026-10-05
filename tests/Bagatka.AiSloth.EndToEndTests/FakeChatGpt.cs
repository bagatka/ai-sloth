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
/// OpenAI's authorization server for Sign in with ChatGPT at the HTTP boundary, strict where OpenAI's
/// is: a first sign-in registers a client, the code works once and only with its PKCE verifier, and
/// every refresh replaces the refresh token and refuses one used twice. Following the sign-in URL
/// signs in at once, as a person who allows everything asked. Every token set asks to be renewed
/// right away, so every model call on a plan renews it.
/// </summary>
internal sealed class FakeChatGpt : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly Lock _lock = new Lock();
    private readonly Dictionary<string, PendingCode> _codes = new Dictionary<string, PendingCode>(StringComparer.Ordinal);
    private readonly Dictionary<string, Registration> _registrations = new Dictionary<string, Registration>(StringComparer.Ordinal);
    private readonly HashSet<string> _accessTokens = new HashSet<string>(StringComparer.Ordinal);
    private int _issued;

    private FakeChatGpt(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Where it runs, such as <c>http://127.0.0.1:41236/</c>.</summary>
    public Uri Url { get; private set; } = new Uri("http://localhost");

    /// <summary>How many refreshes it answered with a new token set.</summary>
    public int Renewals { get; private set; }

    /// <summary>How many refreshes came with a refresh token already used: renewals that raced.</summary>
    public int ReusedRefreshTokens { get; private set; }

    private string Issuer => Url.AbsoluteUri.TrimEnd('/');

    public static async Task<FakeChatGpt> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        WebApplication app = builder.Build();
        FakeChatGpt chatGpt = new FakeChatGpt(app);
        app.MapGet("/api/accounts/authorize", chatGpt.Authorize);
        app.MapPost("/api/accounts/oauth/token", chatGpt.TokenAsync);
        app.MapPost("/api/accounts/oauth/revoke", chatGpt.RevokeAsync);
        await app.StartAsync();
        IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        if (addresses is null)
        {
            throw new InvalidOperationException("Kestrel reported no addresses.");
        }

        chatGpt.Url = new Uri(addresses.Addresses.Single());
        return chatGpt;
    }

    /// <summary>
    /// The person's browser at ChatGPT: opens the sign-in URL, signs in allowing everything asked, and
    /// returns the address ChatGPT sends the browser back to.
    /// </summary>
    public static async Task<Uri> FollowAsync(Uri signInUrl)
    {
        using HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false, CheckCertificateRevocationList = true };
        using HttpClient browser = new HttpClient(handler);
        using HttpResponseMessage response = await browser.GetAsync(signInUrl, TestContext.Current.CancellationToken);
        if (response.StatusCode != HttpStatusCode.Redirect || response.Headers.Location is null)
        {
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"ChatGPT didn't send the browser back: {(int)response.StatusCode}"));
        }

        return response.Headers.Location;
    }

    /// <summary>Whether it issued this access token.</summary>
    public bool Issued(string accessToken)
    {
        lock (_lock)
        {
            return _accessTokens.Contains(accessToken);
        }
    }

    /// <summary>The person disconnects the app in ChatGPT: the client's refresh tokens stop working.</summary>
    public void Disconnect(string clientId)
    {
        lock (_lock)
        {
            Registration? registration = _registrations.GetValueOrDefault(clientId);
            if (registration is null)
            {
                throw new InvalidOperationException("No client " + clientId + " signed in.");
            }

            registration.Disconnected = true;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    private IResult Authorize(HttpRequest request)
    {
        IQueryCollection query = request.Query;
        string requestedClient = query["client_id"].ToString();
        string redirect = query["redirect_uri"].ToString();
        bool valid = string.Equals(query["response_type"], "code", StringComparison.Ordinal)
            && string.Equals(query["code_challenge_method"], "S256", StringComparison.Ordinal)
            && string.Equals(query["resource"], "https://api.openai.com/v1", StringComparison.Ordinal)
            && query["ext_agent_host_id"].ToString().StartsWith("urn:uuid:", StringComparison.Ordinal)
            && redirect.StartsWith("http://127.0.0.1:", StringComparison.Ordinal);
        if (!valid)
        {
            return Results.BadRequest();
        }

        string code = RandomValue();
        string clientId;
        lock (_lock)
        {
            clientId = string.Equals(requestedClient, "dynamic_agent_client", StringComparison.Ordinal)
                ? "oaiapp_" + RandomValue()[..12]
                : requestedClient;
            _registrations.TryAdd(clientId, new Registration("user-" + RandomValue()[..8]));
            _codes[code] = new PendingCode(clientId, redirect, query["code_challenge"].ToString(), query["nonce"].ToString(), query["scope"].ToString());
        }

        string location = redirect
            + "?code=" + Uri.EscapeDataString(code)
            + "&scope=" + Uri.EscapeDataString(query["scope"].ToString())
            + "&state=" + Uri.EscapeDataString(query["state"].ToString())
            + "&client_id=" + Uri.EscapeDataString(clientId);
        return Results.Redirect(location);
    }

    private async Task<IResult> TokenAsync(HttpRequest request)
    {
        IFormCollection form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
        string grant = form["grant_type"].ToString();
        lock (_lock)
        {
            if (string.Equals(grant, "authorization_code", StringComparison.Ordinal))
            {
                return Exchange(form);
            }

            if (string.Equals(grant, "refresh_token", StringComparison.Ordinal))
            {
                return Refresh(form);
            }
        }

        return Refused("unsupported_grant_type");
    }

    private IResult Exchange(IFormCollection form)
    {
        PendingCode? pending = _codes.GetValueOrDefault(form["code"].ToString());
        _codes.Remove(form["code"].ToString());
        string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
        bool genuine = pending is not null
            && string.Equals(pending.ClientId, form["client_id"], StringComparison.Ordinal)
            && string.Equals(pending.RedirectUri, form["redirect_uri"], StringComparison.Ordinal)
            && string.Equals(pending.Challenge, challenge, StringComparison.Ordinal)
            && string.Equals(form["resource"], "https://api.openai.com/v1", StringComparison.Ordinal);
        if (!genuine)
        {
            return Refused("invalid_grant");
        }

        Registration registration = _registrations.GetValueOrDefault(pending!.ClientId)!;
        return Tokens(registration, pending.Scope, IdToken(pending.ClientId, registration, pending.Nonce));
    }

    private IResult Refresh(IFormCollection form)
    {
        string clientId = form["client_id"].ToString();
        Registration? registration = _registrations.GetValueOrDefault(clientId);
        string refreshToken = form["refresh_token"].ToString();
        if (registration is null || registration.Disconnected)
        {
            return Refused("invalid_grant");
        }

        if (registration.UsedRefreshTokens.Contains(refreshToken))
        {
            ReusedRefreshTokens++;
            return Refused("refresh_token_reused");
        }

        if (!string.Equals(registration.RefreshToken, refreshToken, StringComparison.Ordinal))
        {
            return Refused("invalid_grant");
        }

        registration.UsedRefreshTokens.Add(refreshToken);
        Renewals++;
        return Tokens(registration, registration.Scope, idToken: null);
    }

    private async Task<IResult> RevokeAsync(HttpRequest request)
    {
        IFormCollection form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
        lock (_lock)
        {
            Registration? registration = _registrations.GetValueOrDefault(form["client_id"].ToString());
            if (registration is not null && string.Equals(registration.RefreshToken, form["token"], StringComparison.Ordinal))
            {
                registration.Disconnected = true;
            }
        }

        return Results.Ok();
    }

    private IResult Tokens(Registration registration, string scope, string? idToken)
    {
        _issued++;
        string accessToken = "access-" + _issued.ToString(CultureInfo.InvariantCulture) + "-" + RandomValue()[..8];
        registration.RefreshToken = "refresh-" + RandomValue();
        registration.Scope = scope;
        _accessTokens.Add(accessToken);
        JsonObject body = new JsonObject
        {
            ["access_token"] = accessToken,
            ["refresh_token"] = registration.RefreshToken,
            ["token_type"] = "Bearer",
            ["expires_in"] = 3600,
            ["scope"] = scope,
            ["earliest_refresh_at"] = TimeProvider.System.GetUtcNow().ToUnixTimeSeconds(),
        };
        if (idToken is not null)
        {
            body["id_token"] = idToken;
        }

        return Results.Text(body.ToJsonString(), "application/json");
    }

    // An unsigned token: the client reads it, as it came straight from the token endpoint.
    private string IdToken(string clientId, Registration registration, string nonce)
    {
        long now = TimeProvider.System.GetUtcNow().ToUnixTimeSeconds();
        JsonObject payload = new JsonObject
        {
            ["iss"] = Issuer,
            ["sub"] = registration.Subject,
            ["aud"] = clientId,
            ["iat"] = now,
            ["exp"] = now + 3600,
            ["nonce"] = nonce,
            ["email"] = registration.Subject + "@example.com",
        };
        return Base64Url.EncodeToString("""{"alg":"none","typ":"JWT"}"""u8) + "." + Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload.ToJsonString())) + ".unsigned";
    }

    private static IResult Refused(string error)
    {
        return Results.Json(new JsonObject { ["error"] = error, ["error_description"] = "Refused by the fake: " + error }, statusCode: StatusCodes.Status400BadRequest);
    }

    private static string RandomValue()
    {
        return Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24));
    }

    private sealed record PendingCode(string ClientId, string RedirectUri, string Challenge, string Nonce, string Scope);

    private sealed class Registration(string subject)
    {
        public string Subject { get; } = subject;

        public string RefreshToken { get; set; } = string.Empty;

        public string Scope { get; set; } = string.Empty;

        public HashSet<string> UsedRefreshTokens { get; } = new HashSet<string>(StringComparer.Ordinal);

        public bool Disconnected { get; set; }
    }
}
