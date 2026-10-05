namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>
/// Who commits in a person's nooks name: the author and the committer, and the co-author line added to
/// every message, if any.
/// </summary>
/// <param name="Author">Who wrote the change.</param>
/// <param name="Committer">Who committed it.</param>
/// <param name="CoAuthor">The trailer that credits AiSloth, such as <c>Co-authored-by: AiSloth &lt;…&gt;</c>, or <see langword="null"/>.</param>
public sealed record CommitIdentity(GitIdentity Author, GitIdentity Committer, string? CoAuthor);
