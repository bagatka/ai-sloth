using System;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Sdk.GitHub;

namespace Bagatka.AiSloth.Sources.Model;

// A GitHub repository a workspace connected. Its name is its folder in nooks; GitHub's names are the
// ones it gave when added.
internal sealed class Repository
{
    // Used by Add and by EF: parameter names match property names.
    private Repository(RepositoryId id, WorkspaceId workspaceId, string owner, string name, string defaultBranch, Uri url, UserId addedBy, DateTimeOffset addedAt)
    {
        Id = id;
        WorkspaceId = workspaceId;
        Owner = owner;
        Name = name;
        DefaultBranch = defaultBranch;
        Url = url;
        AddedBy = addedBy;
        AddedAt = addedAt;
    }

    public RepositoryId Id { get; private set; }

    public WorkspaceId WorkspaceId { get; private set; }

    public string Owner { get; private set; }

    public string Name { get; private set; }

    public string DefaultBranch { get; private set; }

    public Uri Url { get; private set; }

    public UserId AddedBy { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public static Repository Add(WorkspaceId workspaceId, GitHubRepository repository, UserId addedBy, TimeProvider time)
    {
        return new Repository(RepositoryId.New(), workspaceId, repository.Owner, repository.Name, repository.DefaultBranch, repository.HtmlUrl, addedBy, time.GetUtcNow());
    }

    // GitHub knows the default branch best; it may have changed since the repository was added.
    public void Refresh(GitHubRepository repository)
    {
        DefaultBranch = repository.DefaultBranch;
    }

    public RepositorySummary ToSummary()
    {
        return new RepositorySummary(Id, WorkspaceId, Name, Owner + "/" + Name, DefaultBranch, Url, AddedBy, AddedAt);
    }
}
