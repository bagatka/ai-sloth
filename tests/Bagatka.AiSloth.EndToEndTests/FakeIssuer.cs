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
/// A tiny OpenID Connect provider at the HTTP boundary (PATTERNS.md, entry 12): it serves a discovery
/// document and its signing key, and issues a token for any subject. The WebApi validates these tokens
/// exactly as it validates WorkOS's.
/// </summary>
internal sealed class FakeIssuer : IAsyncDisposable
{
    public const string Audience = "aisloth-e2e";

    private static readonly string[] ResponseTypes = ["code"];
    private static readonly string[] SubjectTypes = ["public"];
    private static readonly string[] SigningAlgorithms = ["RS256"];

    private readonly WebApplication _app;
    private readonly RSA _rsa;
    private readonly RsaSecurityKey _key;

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
        await app.StartAsync();
        IServerAddressesFeature addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel reported no addresses.");
        issuer.Issuer = addresses.Addresses.Single();
        return issuer;
    }

    public string TokenFor(string subject)
    {
        SecurityTokenDescriptor token = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
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
}
