using Bagatka.AiSloth.Sources.Contracts;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>What one of a nook's repositories changed since it was copied in.</summary>
/// <param name="Source">The source, by name: the repository is at <c>/work/{Source}</c>.</param>
/// <param name="Repository">The workspace's repository the source is a copy of.</param>
/// <param name="BaseBranch">The branch it was copied in from.</param>
/// <param name="BaseCommit">The commit it was copied in at, which the changes are against.</param>
/// <param name="Patch">
/// The changes as <c>git diff</c> prints them, one section per changed file, its commits since and
/// what isn't committed together, new files included; empty without changes. A binary file's
/// section says only that it differs.
/// </param>
/// <param name="Truncated">
/// Whether the patch leaves out files at its end, because all of them would be more than
/// <see cref="MaxPatch"/> bytes; it never ends inside a file.
/// </param>
public sealed record SourceChanges(string Source, RepositoryId Repository, string BaseBranch, string BaseCommit, string Patch, bool Truncated)
{
    /// <summary>The most a patch comes to, in UTF-8 bytes.</summary>
    public const int MaxPatch = 2 * 1024 * 1024;
}
