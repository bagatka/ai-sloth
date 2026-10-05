namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>Input to <see cref="ISourcesApi.PushAsync"/>.</summary>
/// <param name="Repository">The repository.</param>
/// <param name="BaseBranch">The branch the changes started from, which a pull request targets.</param>
/// <param name="BaseCommit">The commit the changes started from: the bundle's prerequisite.</param>
/// <param name="Branch">The branch to push to: never the default branch, and only ever moved forward.</param>
/// <param name="PullRequest">Whether to open a pull request into the base branch, or find the one open.</param>
/// <param name="Description">The pull request's description, in Markdown.</param>
public sealed record PushChanges(RepositoryId Repository, string BaseBranch, string BaseCommit, string Branch, bool PullRequest, string Description);
