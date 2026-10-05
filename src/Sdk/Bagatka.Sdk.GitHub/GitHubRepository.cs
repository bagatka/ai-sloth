using System;

namespace Bagatka.Sdk.GitHub;

/// <summary>A repository.</summary>
/// <param name="Owner">The account or organization that owns it.</param>
/// <param name="Name">Its name.</param>
/// <param name="Private">Whether only people given access see it.</param>
/// <param name="DefaultBranch">The branch pull requests target by default, such as <c>main</c>.</param>
/// <param name="CloneUrl">Where git clones it over HTTPS.</param>
/// <param name="HtmlUrl">Its page.</param>
public sealed record GitHubRepository(string Owner, string Name, bool Private, string DefaultBranch, Uri CloneUrl, Uri HtmlUrl)
{
    /// <summary><c>owner/name</c>.</summary>
    public string FullName => Owner + "/" + Name;
}
