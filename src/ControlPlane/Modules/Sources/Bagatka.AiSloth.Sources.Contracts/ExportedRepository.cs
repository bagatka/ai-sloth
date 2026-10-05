using System;

namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>A copy of a repository's branch, written as a git bundle.</summary>
/// <param name="Name">The repository's folder name.</param>
/// <param name="Branch">The branch copied.</param>
/// <param name="Commit">The commit it was at.</param>
/// <param name="Url">The repository's address for <c>origin</c>, without credentials.</param>
public sealed record ExportedRepository(string Name, string Branch, string Commit, Uri Url);
