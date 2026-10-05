using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A tiny OpenID Connect provider at the HTTP boundary (PATTERNS.md, entry 12), for the host's sign-in
/// with a provider: discovery, signing keys, and the authorization code flow, whose authorize step signs
/// in whoever <c>login_hint</c> names at once. The WebApi checks its ID tokens exactly as it checks
/// WorkOS's. It can also sign a token for anyone, to show such tokens aren't sessions.
/// </summary>
internal sealed class FakeIssuer : IAsyncDisposable
{
    public const string ClientId = "aisloth-e2e";
    public const string ClientSecret = "e2e-client-secret";

    private static readonly string[] ResponseTypes = ["code"];
    private static readonly string[] SubjectTypes = ["public"];
    private static readonly string[] SigningAlgorithms = ["RS256"];

    private readonly WebApplication _app;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _key;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, PendingCode> _codes = new System.Collections.Concurrent.ConcurrentDictionary<string, PendingCode>(StringComparer.Ordinal);

    private FakeIssuer(WebApplication app, RSA rsa)
    {
        _app = app;
        _rsa = rsa;
        _key = new RsaSecurityKey(rsa) { KeyId = "e2e" };
    }

    /// <summary>The issuer as tokens carry it, such as <c>http://127.0.0.1:41234</c>.</summary>
    public string Issuer { get; private set; } = string.Empty;

    public static async Task<FakeIssuer> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        WebApplication app = builder.Build();
        FakeIssuer issuer = new FakeIssuer(app, RSA.Create(2048));
        app.MapGet("/.well-known/openid-configuration", issuer.Discovery);
        app.MapGet("/jwks", issuer.Keys);
        app.MapGet("/authorize", issuer.Authorize);
        app.MapPost("/token", issuer.TokenAsync);
        await app.StartAsync();
        IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        if (addresses is null)
        {
            throw new InvalidOperationException("Kestrel reported no addresses.");
        }

        issuer.Issuer = addresses.Addresses.Single();
        return issuer;
    }

    public string TokenFor(string subject)
    {
        SecurityTokenDescriptor token = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = subject },
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        };
        return new JsonWebTokenHandler().CreateToken(token);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        _rsa.Dispose();
    }

    private IResult Discovery()
    {
        return Results.Json(new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["issuer"] = Issuer,
            ["jwks_uri"] = Issuer + "/jwks",
            ["authorization_endpoint"] = Issuer + "/authorize",
            ["token_endpoint"] = Issuer + "/token",
            ["response_types_supported"] = ResponseTypes,
            ["subject_types_supported"] = SubjectTypes,
            ["id_token_signing_alg_values_supported"] = SigningAlgorithms,
        });
    }

    private IResult Keys()
    {
        RSAParameters key = _rsa.ExportParameters(includePrivateParameters: false);
        Dictionary<string, string> jwk = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["kty"] = "RSA",
            ["use"] = "sig",
            ["alg"] = "RS256",
            ["kid"] = _key.KeyId,
            ["n"] = Base64UrlEncoder.Encode(key.Modulus),
            ["e"] = Base64UrlEncoder.Encode(key.Exponent),
        };
        return Results.Json(new Dictionary<string, object>(StringComparer.Ordinal) { ["keys"] = new[] { jwk } });
    }

    // Signs in whoever login_hint names, at once, and sends the browser back with a code.
    private IResult Authorize(HttpRequest request)
    {
        IQueryCollection query = request.Query;
        string subject = query["login_hint"].ToString();
        bool valid = string.Equals(query["client_id"], ClientId, StringComparison.Ordinal)
            && string.Equals(query["response_type"], "code", StringComparison.Ordinal)
            && string.Equals(query["code_challenge_method"], "S256", StringComparison.Ordinal)
            && subject.Length > 0;
        if (!valid)
        {
            return Results.BadRequest();
        }

        string code = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(24));
        _codes[code] = new PendingCode(subject, query["nonce"].ToString(), query["redirect_uri"].ToString(), query["code_challenge"].ToString());
        return Results.Redirect(query["redirect_uri"] + "?code=" + Uri.EscapeDataString(code) + "&state=" + Uri.EscapeDataString(query["state"].ToString()));
    }

    // A code works once, for the confidential client with its secret and the PKCE verifier.
    private async Task<IResult> TokenAsync(HttpRequest request)
    {
        IFormCollection form = await request.ReadFormAsync(request.HttpContext.RequestAborted);
        bool found = _codes.TryRemove(form["code"].ToString(), out PendingCode? pending);
        string challenge = Base64UrlEncoder.Encode(SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(form["code_verifier"].ToString())));
        bool genuine = found
            && string.Equals(form["client_id"], ClientId, StringComparison.Ordinal)
            && string.Equals(form["client_secret"], ClientSecret, StringComparison.Ordinal)
            && string.Equals(form["redirect_uri"], pending!.RedirectUri, StringComparison.Ordinal)
            && string.Equals(challenge, pending.Challenge, StringComparison.Ordinal);
        if (!genuine)
        {
            return Results.Json(new Dictionary<string, string>(StringComparer.Ordinal) { ["error"] = "invalid_grant" }, statusCode: StatusCodes.Status400BadRequest);
        }

        SecurityTokenDescriptor token = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = ClientId,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal) { ["sub"] = pending!.Subject, ["nonce"] = pending.Nonce, ["name"] = pending.Subject },
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.RsaSha256),
        };
        string idToken = new JsonWebTokenHandler().CreateToken(token);
        return Results.Json(new Dictionary<string, string>(StringComparer.Ordinal) { ["id_token"] = idToken, ["access_token"] = "unused", ["token_type"] = "Bearer" });
    }

    private sealed record PendingCode(string Subject, string Nonce, string RedirectUri, string Challenge);
}
