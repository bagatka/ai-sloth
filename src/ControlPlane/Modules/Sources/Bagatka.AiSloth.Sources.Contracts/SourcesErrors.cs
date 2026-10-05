using Bagatka.Foundation;

namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>
/// Errors callers of <see cref="ISourcesApi"/> may branch on.
/// </summary>
public static class SourcesErrors
{
    /// <summary>The repository doesn't exist, or the actor may not learn that it exists.</summary>
    public static readonly Error RepositoryNotFound = Error.NotFound("sources.repository_not_found", "Repository not found.");

    /// <summary>The workspace already has this repository.</summary>
    public static readonly Error RepositoryAlreadyAdded = Error.Conflict("sources.repository_already_added", "The workspace already has this repository.");

    /// <summary>The host has no GitHub App: its operator configures one.</summary>
    public static readonly Error GitHubNotConfigured = Error.Conflict("sources.github_not_configured", "This host isn't set up for GitHub; its operator gives it a GitHub App.");

    /// <summary>The person hasn't connected GitHub, or GitHub ended the connection: connect again.</summary>
    public static readonly Error GitHubNotConnected = Error.Conflict("sources.github_not_connected", "Connect your GitHub account first.");

    /// <summary>The connection attempt doesn't exist, isn't the caller's, or expired: start again.</summary>
    public static readonly Error ConnectionAttemptNotFound = Error.NotFound("sources.connection_attempt_not_found", "The GitHub connection expired or was refused; start again.");

    /// <summary>The person's GitHub connection can't see the repository: install the app on it.</summary>
    public static readonly Error RepositoryUnreachable = Error.NotFound("sources.repository_unreachable", "Your GitHub connection can't reach this repository; install the app on it.");

    /// <summary>The branch moved on at GitHub in a way the push would undo; push to another branch.</summary>
    public static readonly Error NotFastForward = Error.Conflict("sources.not_fast_forward", "The branch has commits the changes don't include; push to another branch.");
}
