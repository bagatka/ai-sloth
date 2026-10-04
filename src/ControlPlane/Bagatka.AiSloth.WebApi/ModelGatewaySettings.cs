using System;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// Where the model gateway forwards agents' calls, and the key it adds to them. The key never
/// leaves the control plane.
/// </summary>
internal sealed record ModelGatewaySettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="upstream">The model provider's API, such as <c>https://api.anthropic.com</c>; plain HTTP only on this machine, for tests.</param>
    /// <param name="apiKey">The deployment's key for that API.</param>
    public ModelGatewaySettings(Uri upstream, string apiKey)
    {
        ArgumentNullException.ThrowIfNull(upstream);
        bool secure = upstream.IsAbsoluteUri
            && (string.Equals(upstream.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || (string.Equals(upstream.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && upstream.IsLoopback));
        if (!secure)
        {
            throw new ArgumentException("The upstream must be an https URL, or http on this machine.", nameof(upstream));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

        // A trailing slash makes the forwarded paths relative to it.
        Upstream = upstream.AbsoluteUri.EndsWith('/', StringComparison.Ordinal) ? upstream : new Uri(upstream.AbsoluteUri + "/");
        ApiKey = apiKey;
    }

    /// <summary>The model provider's API, ending with a slash.</summary>
    public Uri Upstream { get; }

    /// <summary>The deployment's key for that API.</summary>
    public string ApiKey { get; }
}
