using System;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// The OpenID Connect provider people may sign in with, when the host has one: WorkOS on the hosted
/// service, a company's Entra, Okta, or Google. The host is its confidential client; clients never
/// talk to it.
/// </summary>
internal sealed record SignInProviderSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="issuer">The provider's issuer, whose discovery document lists its endpoints and keys: https, or http on this machine for tests.</param>
    /// <param name="clientId">The host's client ID at the provider.</param>
    /// <param name="clientSecret">The host's client secret at the provider.</param>
    /// <param name="name">What people see on the sign-in button, such as <c>Google</c>.</param>
    public SignInProviderSettings(Uri issuer, string clientId, string clientSecret, string name)
    {
        ArgumentNullException.ThrowIfNull(issuer);
        bool secure = issuer.IsAbsoluteUri
            && (string.Equals(issuer.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || (string.Equals(issuer.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && issuer.IsLoopback));
        if (!secure)
        {
            throw new ArgumentException("The issuer must be an https URL, or http on this machine.", nameof(issuer));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Issuer = issuer;
        ClientId = clientId;
        ClientSecret = clientSecret;
        Name = name;
    }

    /// <summary>The provider's issuer.</summary>
    public Uri Issuer { get; }

    /// <summary>The host's client ID at the provider.</summary>
    public string ClientId { get; }

    /// <summary>The host's client secret at the provider.</summary>
    public string ClientSecret { get; }

    /// <summary>What people see on the sign-in button.</summary>
    public string Name { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return "SignInProviderSettings { Issuer = " + Issuer.AbsoluteUri + ", ClientSecret = *** }";
    }
}
