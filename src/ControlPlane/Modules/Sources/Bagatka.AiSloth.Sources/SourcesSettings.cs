namespace Bagatka.AiSloth.Sources;

/// <summary>
/// What the Sources module needs from its host.
/// </summary>
public sealed record SourcesSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="gitHubApp">The host's GitHub App, or <see langword="null"/> for a host without one.</param>
    public SourcesSettings(GitHubAppSettings? gitHubApp = null)
    {
        GitHubApp = gitHubApp;
    }

    /// <summary>The host's GitHub App, if it has one.</summary>
    public GitHubAppSettings? GitHubApp { get; }
}
