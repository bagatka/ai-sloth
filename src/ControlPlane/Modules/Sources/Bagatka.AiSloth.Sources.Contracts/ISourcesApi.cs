using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>
/// Sources: where a nook's files come from, and where its changes go. People connect their GitHub
/// account through the host's GitHub App; a workspace's managers add repositories its nooks start
/// with; the control plane moves code in and out with the person's connection, so no nook ever holds
/// it. Pushes only create branches or move them forward, and never touch the default branch.
/// </summary>
public interface ISourcesApi
{
    /// <summary>
    /// Starts connecting the caller's GitHub account with GitHub's device flow: they enter the code at
    /// GitHub, and the caller completes the connection until they have.
    /// </summary>
    /// <returns>The attempt; <see cref="SourcesErrors.GitHubNotConfigured"/>; or unauthorized for an actor that isn't a user.</returns>
    public Task<Result<GitHubConnectionStarted>> StartGitHubConnectionAsync(Actor actor, CancellationToken ct);

    /// <summary>
    /// Asks GitHub whether the person approved, and connects the account once they have, replacing any
    /// earlier connection. Asking sooner than the attempt's interval waits until it has passed.
    /// </summary>
    /// <returns>The progress; or <see cref="SourcesErrors.ConnectionAttemptNotFound"/> once it expired or was refused.</returns>
    public Task<Result<GitHubConnectionProgress>> CompleteGitHubConnectionAsync(Actor actor, GitHubConnectionAttemptId id, CancellationToken ct);

    /// <summary>The caller's connected GitHub account.</summary>
    /// <returns>The account; or <see cref="SourcesErrors.GitHubNotConnected"/>.</returns>
    public Task<Result<GitHubAccount>> GetGitHubAccountAsync(Actor actor, CancellationToken ct);

    /// <summary>Forgets the caller's GitHub connection. Their pushes stop until they connect again; the app stays installed.</summary>
    /// <returns>Success, also when nothing was connected; or unauthorized for an actor that isn't a user.</returns>
    public Task<Result> DisconnectGitHubAsync(Actor actor, CancellationToken ct);

    /// <summary>The repositories the caller's GitHub connection reaches, to add to a workspace. Calls GitHub.</summary>
    /// <returns>The repositories; or <see cref="SourcesErrors.GitHubNotConnected"/>.</returns>
    public Task<Result<AvailableRepositories>> ListAvailableRepositoriesAsync(Actor actor, CancellationToken ct);

    /// <summary>
    /// Adds a repository the caller's GitHub connection reaches to the workspace. Managers only. Its
    /// folder name is its own name, which the workspace's repositories share one of.
    /// </summary>
    /// <returns>
    /// The repository; <see cref="SourcesErrors.RepositoryUnreachable"/>; <see cref="SourcesErrors.RepositoryAlreadyAdded"/>;
    /// <see cref="SourcesErrors.GitHubNotConnected"/>; or the workspace's not found or forbidden.
    /// </returns>
    public Task<Result<RepositorySummary>> AddRepositoryAsync(Actor actor, AddRepository command, CancellationToken ct);

    /// <summary>The workspace's repositories, by name.</summary>
    /// <returns>The repositories; or the workspace's not found.</returns>
    public Task<Result<IReadOnlyList<RepositorySummary>>> ListRepositoriesAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>A repository, for anyone who sees its workspace and the control plane's own processes.</summary>
    /// <returns>The repository; or <see cref="SourcesErrors.RepositoryNotFound"/>.</returns>
    public Task<Result<RepositorySummary>> GetRepositoryAsync(Actor actor, RepositoryId id, CancellationToken ct);

    /// <summary>Removes a repository from its workspace; nooks that have it keep their copy. Managers only.</summary>
    /// <returns>Success; <see cref="SourcesErrors.RepositoryNotFound"/>; or forbidden.</returns>
    public Task<Result> RemoveRepositoryAsync(Actor actor, RepositoryId id, CancellationToken ct);

    /// <summary>How the caller's commits and branches look.</summary>
    /// <returns>The settings, the defaults until they choose; or unauthorized for an actor that isn't a user.</returns>
    public Task<Result<GitSettings>> GetGitSettingsAsync(Actor actor, CancellationToken ct);

    /// <summary>Sets how the caller's commits and branches look, for nooks started and pushes made from now on.</summary>
    /// <returns>The settings; a validation error for an invalid identity or prefix; or unauthorized.</returns>
    public Task<Result<GitSettings>> SetGitSettingsAsync(Actor actor, SetGitSettings command, CancellationToken ct);

    /// <summary>
    /// Writes a branch of the repository, with its history, to <paramref name="destination"/> as a git
    /// bundle, fetched from GitHub with the actor's connection. For copying a repository into a nook.
    /// </summary>
    /// <returns>
    /// What was written; <see cref="SourcesErrors.RepositoryNotFound"/>; <see cref="SourcesErrors.RepositoryUnreachable"/>;
    /// <see cref="SourcesErrors.GitHubNotConnected"/>; or a validation error for a branch that doesn't exist.
    /// </returns>
    public Task<Result<ExportedRepository>> ExportAsync(Actor actor, ExportRepository command, Stream destination, CancellationToken ct);

    /// <summary>
    /// Pushes the commits in <paramref name="bundle"/>, a git bundle of the changes since the base
    /// commit, to a branch of the repository with the actor's connection, and opens a pull request when
    /// asked. People with Write on the repository's workspace only. The branch is created, or moved
    /// forward if the changes include everything on it; it is never the default branch, and nothing is
    /// ever force-pushed.
    /// </summary>
    /// <returns>
    /// What was pushed; <see cref="SourcesErrors.NotFastForward"/>; a validation error for an invalid
    /// branch or the default branch; <see cref="SourcesErrors.RepositoryNotFound"/>;
    /// <see cref="SourcesErrors.GitHubNotConnected"/>; or a conflict with GitHub's words when it refuses.
    /// </returns>
    public Task<Result<PushedChanges>> PushAsync(Actor actor, PushChanges command, Stream bundle, CancellationToken ct);
}
