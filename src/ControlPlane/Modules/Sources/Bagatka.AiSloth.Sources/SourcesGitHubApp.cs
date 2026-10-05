using System;

namespace Bagatka.AiSloth.Sources;

/// <summary>
/// The host's GitHub App, which people connect their GitHub account through. Its "Enable Device Flow"
/// setting is on, and it may read and write contents and pull requests. Each host has its own:
/// <c>sloth github create-app</c> makes one.
/// </summary>
public sealed record SourcesGitHubApp
{
    /// <summary>Creates the settings.</summary>
    /// <param name="clientId">The app's client ID, such as <c>Iv23li…</c>.</param>
    /// <param name="clientSecret">One of the app's client secrets, which renews people's tokens.</param>
    /// <param name="slug">The app's name in URLs, such as <c>aisloth</c>: where people install it, and its bot's login.</param>
    public SourcesGitHubApp(string clientId, string clientSecret, string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ClientId = clientId;
        ClientSecret = clientSecret;
        Slug = slug;
    }

    /// <summary>The app's client ID.</summary>
    public string ClientId { get; }

    /// <summary>One of the app's client secrets.</summary>
    public string ClientSecret { get; }

    /// <summary>The app's name in URLs.</summary>
    public string Slug { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        return "SourcesGitHubApp { ClientId = " + ClientId + ", Slug = " + Slug + ", ClientSecret = *** }";
    }
}
