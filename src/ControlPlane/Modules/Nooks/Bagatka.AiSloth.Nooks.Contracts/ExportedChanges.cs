using Bagatka.AiSloth.Sources.Contracts;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>A repository's changes in a nook, written as a git bundle when there are any.</summary>
/// <param name="Repository">The workspace's repository the source is a copy of.</param>
/// <param name="BaseBranch">The branch the source started from.</param>
/// <param name="BaseCommit">The commit it started from: the bundle's prerequisite.</param>
/// <param name="HasChanges">Whether there were commits beyond it, after committing what wasn't; without any, nothing was written.</param>
public sealed record ExportedChanges(RepositoryId Repository, string BaseBranch, string BaseCommit, bool HasChanges);
