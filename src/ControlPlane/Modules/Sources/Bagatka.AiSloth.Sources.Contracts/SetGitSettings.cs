namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>Input to <see cref="ISourcesApi.SetGitSettingsAsync"/>: the person's choices, all at once.</summary>
/// <param name="Author">Who commits name as their author; <see langword="null"/> for the person's GitHub account.</param>
/// <param name="Committer">Who commits name as their committer; <see langword="null"/> for the author.</param>
/// <param name="AiSlothCoAuthor">Whether every commit credits AiSloth as its co-author.</param>
/// <param name="BranchPrefix">
/// What branches AiSloth names start with: up to 50 characters of letters, digits, <c>/</c>, <c>-</c>,
/// <c>_</c>, and <c>.</c>; empty for none.
/// </param>
public sealed record SetGitSettings(GitIdentity? Author, GitIdentity? Committer, bool AiSlothCoAuthor, string BranchPrefix);
