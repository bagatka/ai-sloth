using System;

namespace Bagatka.Sdk.OpenRouter;

/// <summary>
/// Where OpenRouter's API is.
/// </summary>
public sealed record OpenRouterSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="apiUrl">The API's root, <c>https://openrouter.ai/api</c>; plain HTTP only on this machine, for tests.</param>
    public OpenRouterSettings(Uri apiUrl)
    {
        ArgumentNullException.ThrowIfNull(apiUrl);
        bool secure = apiUrl.IsAbsoluteUri
            && (string.Equals(apiUrl.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || (string.Equals(apiUrl.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && apiUrl.IsLoopback));
        if (!secure)
        {
            throw new ArgumentException("Must be an https URL, or http on this machine.", nameof(apiUrl));
        }

        ApiUrl = new Uri(apiUrl.AbsoluteUri.TrimEnd('/') + "/");
    }

    /// <summary>OpenRouter's own.</summary>
    public static OpenRouterSettings Public { get; } = new OpenRouterSettings(new Uri("https://openrouter.ai/api"));

    /// <summary>The API's root, ending with a slash.</summary>
    public Uri ApiUrl { get; }
}
