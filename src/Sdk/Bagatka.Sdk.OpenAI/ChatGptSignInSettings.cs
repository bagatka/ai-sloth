using System;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// Where Sign in with ChatGPT runs.
/// </summary>
public sealed record ChatGptSignInSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="authority">
    /// OpenAI's authorization server, <c>https://auth.openai.com</c>; plain HTTP only on this machine,
    /// for tests.
    /// </param>
    public ChatGptSignInSettings(Uri authority)
    {
        ArgumentNullException.ThrowIfNull(authority);
        bool secure = authority.IsAbsoluteUri
            && (string.Equals(authority.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || (string.Equals(authority.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal) && authority.IsLoopback));
        if (!secure)
        {
            throw new ArgumentException("The authority must be an https URL, or http on this machine.", nameof(authority));
        }

        Authority = new Uri(authority.GetLeftPart(UriPartial.Authority) + "/");
    }

    /// <summary>OpenAI's authorization server, ending with a slash.</summary>
    public Uri Authority { get; }
}
