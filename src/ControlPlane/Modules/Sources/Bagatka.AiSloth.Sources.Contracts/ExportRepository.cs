namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>Input to <see cref="ISourcesApi.ExportAsync"/>.</summary>
/// <param name="Repository">The repository.</param>
/// <param name="Branch">The branch to copy, or <see langword="null"/> for the default branch.</param>
public sealed record ExportRepository(RepositoryId Repository, string? Branch);
