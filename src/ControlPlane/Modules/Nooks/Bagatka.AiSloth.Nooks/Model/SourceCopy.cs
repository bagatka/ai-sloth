using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Sources.Contracts;

namespace Bagatka.AiSloth.Nooks.Model;

// A nook's copy of a source: a repository at /work/<name>, planned when the nook is created and
// copied in before anything runs there, after which its branch and commit are the base its changes
// are pushed on.
internal sealed class SourceCopy
{
    // Used by the factories and by EF: parameter names match property names.
    private SourceCopy(NookId nookId, string name, RepositoryId repositoryId, string? branch, string? commit)
    {
        NookId = nookId;
        Name = name;
        RepositoryId = repositoryId;
        Branch = branch;
        Commit = commit;
    }

    public NookId NookId { get; private set; }

    public string Name { get; private set; }

    public RepositoryId RepositoryId { get; private set; }

    public string? Branch { get; private set; }

    public string? Commit { get; private set; }

    public bool CopiedIn => Commit is not null;

    public static SourceCopy Planned(NookId nookId, string name, RepositoryId repositoryId, string? branch)
    {
        return new SourceCopy(nookId, name, repositoryId, branch, commit: null);
    }

    // The same repository in a nook that starts with a copy of this one's files.
    public SourceCopy CopyTo(NookId nookId)
    {
        return new SourceCopy(nookId, Name, RepositoryId, Branch, Commit);
    }

    public void Copied(string branch, string commit)
    {
        Branch = branch;
        Commit = commit;
    }

    public NookSource ToContract()
    {
        return new NookSource(Name, RepositoryId, Branch, Commit);
    }
}
