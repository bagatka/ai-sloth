using System;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// Where the model gateway forwards agents' calls. The key each call carries is its chat's agent account's.
/// </summary>
internal sealed record ModelGatewaySettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="upstream">The model provider's API, such as <c>https://api.anthropic.com</c>; plain HTTP only on this machine, for tests.</param>
    public ModelGatewaySettings(Uri upstream)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        bool secure = upstream.IsAbsoluteUri
            && (string.Equals(upstream.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || (string.Equals(upstream.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && upstream.IsLoopback));
        if (!secure)
        {
            throw new ArgumentException("The upstream must be an https URL, or http on this machine.", nameof(upstream));
        }

        // A trailing slash makes the forwarded paths relative to it.
        Upstream = upstream.AbsoluteUri.EndsWith('/', StringComparison.Ordinal) ? upstream : new Uri(upstream.AbsoluteUri + "/");
    }

    /// <summary>The model provider's API, ending with a slash.</summary>
    public Uri Upstream { get; }
}
