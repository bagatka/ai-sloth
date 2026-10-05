namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>
/// How a person's commits and branches look. Their choices, and what those amount to.
/// </summary>
/// <param name="Author">Who commits name as their author; <see langword="null"/> for the person's GitHub account.</param>
/// <param name="Committer">Who commits name as their committer; <see langword="null"/> for the author.</param>
/// <param name="AiSlothCoAuthor">Whether every commit credits AiSloth as its co-author.</param>
/// <param name="BranchPrefix">What branches AiSloth names start with, such as <c>aisloth/</c>; a push can name any branch instead.</param>
/// <param name="Effective">
/// What commits get, with the defaults filled in; <see langword="null"/> while the author comes from a
/// GitHub account the person hasn't connected.
/// </param>
public sealed record GitSettings(GitIdentity? Author, GitIdentity? Committer, bool AiSlothCoAuthor, string BranchPrefix, CommitIdentity? Effective);
