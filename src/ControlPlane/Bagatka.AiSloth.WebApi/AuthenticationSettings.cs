using System;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// Which identity provider's tokens the WebApi accepts (ARCHITECTURE.md, "WebApi"): WorkOS for the
/// hosted service, any OpenID Connect provider for self-hosting.
/// </summary>
internal sealed record AuthenticationSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="issuer">
    /// The provider's issuer, whose discovery document lists its signing keys. It must use HTTPS;
    /// plain HTTP is accepted only on this machine, for development and tests.
    /// </param>
    /// <param name="audience">
    /// The audience tokens must name, so tokens the provider issued for other apps are refused.
    /// WorkOS adds it through a JWT template.
    /// </param>
    public AuthenticationSettings(Uri issuer, string audience)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        bool secure = issuer.IsAbsoluteUri
            && (string.Equals(issuer.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || (string.Equals(issuer.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && issuer.IsLoopback));
        if (!secure)
        {
            throw new ArgumentException("The issuer must be an https URL, or http on this machine.", nameof(issuer));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        Issuer = issuer;
        Audience = audience;
    }

    /// <summary>The provider's issuer.</summary>
    public Uri Issuer { get; }

    /// <summary>The audience tokens must name.</summary>
    public string Audience { get; }
}
