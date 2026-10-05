using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// The host's client of its identity provider: OpenID Connect's authorization code flow with PKCE, as
/// a confidential client. It sends people's browsers to the provider and checks who came back.
/// </summary>
internal sealed class SignInProvider
{
    public const string HttpClientName = "sign-in-provider";

    private readonly SignInProviderSettings _settings;
    private readonly IHttpClientFactory _clients;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configuration;

    public SignInProvider(SignInProviderSettings settings, IHttpClientFactory clients)
    {
        _settings = settings;
        _clients = clients;
        string discovery = settings.Issuer.AbsoluteUri.TrimEnd('/') + "/.well-known/openid-configuration";
        bool https = string.Equals(settings.Issuer.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal);
        _configuration = new ConfigurationManager<OpenIdConnectConfiguration>(discovery, new OpenIdConnectConfigurationRetriever(), new HttpDocumentRetriever { RequireHttps = https });
    }

    /// <summary>What people see on the sign-in button.</summary>
    public string Name => _settings.Name;

    /// <summary>Where to send the person's browser; the provider sends it back to <paramref name="callback"/>.</summary>
    public async Task<Uri> AuthorizationUrlAsync(Uri callback, string state, string nonce, string codeChallenge, string? loginHint, CancellationToken ct)
    {
        OpenIdConnectConfiguration configuration = await _configuration.GetConfigurationAsync(ct);
        Dictionary<string, string?> query = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["client_id"] = _settings.ClientId,
            ["response_type"] = "code",
            ["scope"] = "openid profile email",
            ["redirect_uri"] = callback.AbsoluteUri,
            ["state"] = state,
            ["nonce"] = nonce,
            ["code_challenge"] = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["login_hint"] = loginHint,
        };
        string url = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(configuration.AuthorizationEndpoint, query);
        return new Uri(url);
    }

    /// <summary>
    /// Who the provider says signed in: exchanges the code and checks the ID token's signature, issuer,
    /// audience, lifetime, and nonce. <see langword="null"/> when the provider refused the code or its
    /// answer doesn't check out.
    /// </summary>
    public async Task<VerifiedIdentity?> ExchangeAsync(Uri callback, string code, string codeVerifier, string nonce, CancellationToken ct)
    {
        OpenIdConnectConfiguration configuration = await _configuration.GetConfigurationAsync(ct);
        Dictionary<string, string> form = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = callback.AbsoluteUri,
            ["client_id"] = _settings.ClientId,
            ["client_secret"] = _settings.ClientSecret,
            ["code_verifier"] = codeVerifier,
        };
        using FormUrlEncodedContent content = new FormUrlEncodedContent(form);
        using HttpResponseMessage response = await _clients.CreateClient(HttpClientName).PostAsync(new Uri(configuration.TokenEndpoint), content, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using System.IO.Stream stream = await response.Content.ReadAsStreamAsync(ct);
        using JsonDocument body = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        bool found = body.RootElement.TryGetProperty("id_token", out JsonElement idToken);
        if (!found || idToken.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        TokenValidationParameters parameters = new TokenValidationParameters
        {
            ValidIssuer = configuration.Issuer,
            ValidAudience = _settings.ClientId,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidateLifetime = true,
        };
        TokenValidationResult validated = await new JsonWebTokenHandler().ValidateTokenAsync(idToken.GetString(), parameters);
        ClaimsIdentity? identity = validated.IsValid ? validated.ClaimsIdentity : null;
        string? subject = identity?.FindFirst("sub")?.Value;
        bool nonceMatches = string.Equals(identity?.FindFirst("nonce")?.Value, nonce, StringComparison.Ordinal);
        if (identity is null || subject is null || !nonceMatches)
        {
            return null;
        }

        string? name = identity.FindFirst("name")?.Value ?? identity.FindFirst("email")?.Value;
        return new VerifiedIdentity(configuration.Issuer, subject, name);
    }
}
