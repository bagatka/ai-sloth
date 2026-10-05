using Bagatka.AiSloth.Sources.Contracts;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>A repository a new nook starts with.</summary>
/// <param name="Repository">One of the workspace's repositories (<c>ISourcesApi</c>).</param>
/// <param name="Branch">The branch to start from, or <see langword="null"/> for its default branch.</param>
public sealed record NookRepository(RepositoryId Repository, string? Branch = null);
