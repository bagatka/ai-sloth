using System;

namespace Bagatka.Sdk.GitHub;

/// <summary>
/// Where GitHub runs: github.com, or a GitHub Enterprise Server.
/// </summary>
public sealed record GitHubSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="apiUrl">The REST API, <c>https://api.github.com</c>; plain HTTP only on this machine, for tests.</param>
    /// <param name="webUrl">The site people sign in at, <c>https://github.com</c>; plain HTTP only on this machine, for tests.</param>
    public GitHubSettings(Uri apiUrl, Uri webUrl)
    {
        ApiUrl = Validated(apiUrl, nameof(apiUrl));
        WebUrl = Validated(webUrl, nameof(webUrl));
    }

    /// <summary>github.com's.</summary>
    public static GitHubSettings Public { get; } = new GitHubSettings(new Uri("https://api.github.com"), new Uri("https://github.com"));

    /// <summary>The REST API, ending with a slash.</summary>
    public Uri ApiUrl { get; }

    /// <summary>The site people sign in at, ending with a slash.</summary>
    public Uri WebUrl { get; }

    private static Uri Validated(Uri url, string name)
    {
        ArgumentNullException.ThrowIfNull(url, name);
        bool secure = url.IsAbsoluteUri
            && (string.Equals(url.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || (string.Equals(url.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && url.IsLoopback));
        if (!secure)
        {
            throw new ArgumentException("Must be an https URL, or http on this machine.", name);
        }

        return new Uri(url.AbsoluteUri.TrimEnd('/') + "/");
    }
}
