using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Sources.Model;

// How one person's commits and branches look. Absent identities mean the defaults: the author is their
// GitHub account, and the committer the author.
internal sealed class PersonGitSettings
{
    public const string DefaultBranchPrefix = "aisloth/";

    // Used by the factories and by EF: parameter names match property names.
    private PersonGitSettings(UserId userId, bool aiSlothCoAuthor, string branchPrefix)
    {
        UserId = userId;
        AiSlothCoAuthor = aiSlothCoAuthor;
        BranchPrefix = branchPrefix;
    }

    public UserId UserId { get; private set; }

    public string? AuthorName { get; private set; }

    public string? AuthorEmail { get; private set; }

    public string? CommitterName { get; private set; }

    public string? CommitterEmail { get; private set; }

    public bool AiSlothCoAuthor { get; private set; }

    public string BranchPrefix { get; private set; }

    public GitIdentity? Author => AuthorName is null || AuthorEmail is null ? null : new GitIdentity(AuthorName, AuthorEmail);

    public GitIdentity? Committer => CommitterName is null || CommitterEmail is null ? null : new GitIdentity(CommitterName, CommitterEmail);

    // What someone who never chose gets.
    public static PersonGitSettings Defaults(UserId userId)
    {
        return new PersonGitSettings(userId, aiSlothCoAuthor: true, DefaultBranchPrefix);
    }

    public Result Set(SetGitSettings command)
    {
        Error? invalid = command.Author is null ? null : GitNames.CheckIdentity(command.Author, "author");
        invalid ??= command.Committer is null ? null : GitNames.CheckIdentity(command.Committer, "committer");
        if (invalid is null && !GitNames.IsPrefix(command.BranchPrefix))
        {
            invalid = Error.Validation("branchPrefix", "Must be up to 50 characters of letters, digits, /, -, _, and ., starting a valid branch name.");
        }

        if (invalid is not null)
        {
            return new Result(invalid);
        }

        AuthorName = command.Author?.Name;
        AuthorEmail = command.Author?.Email;
        CommitterName = command.Committer?.Name;
        CommitterEmail = command.Committer?.Email;
        AiSlothCoAuthor = command.AiSlothCoAuthor;
        BranchPrefix = command.BranchPrefix;
        return new Result(new Success());
    }

    // The defaults filled in from the person's GitHub account, if connected; the co-author line given
    // only when they want it.
    public GitSettings ToSettings(GitIdentity? gitHubIdentity, string? coAuthor)
    {
        GitIdentity? author = Author ?? gitHubIdentity;
        CommitIdentity? effective = author is null ? null : new CommitIdentity(author, Committer ?? author, AiSlothCoAuthor ? coAuthor : null);
        return new GitSettings(Author, Committer, AiSlothCoAuthor, BranchPrefix, effective);
    }
}
