using Bagatka.AiSloth.Sources.Contracts;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>A repository in a nook, at <c>/work/&lt;name&gt;</c>.</summary>
/// <param name="Name">Its name, which is its folder in <c>/work</c>.</param>
/// <param name="Repository">The workspace's repository it is a copy of.</param>
/// <param name="Branch">The branch it started from; <see langword="null"/> for the default branch until it's copied in.</param>
/// <param name="Commit">The commit it started from, the base of its changes; <see langword="null"/> until it's copied in.</param>
public sealed record NookSource(string Name, RepositoryId Repository, string? Branch, string? Commit);
