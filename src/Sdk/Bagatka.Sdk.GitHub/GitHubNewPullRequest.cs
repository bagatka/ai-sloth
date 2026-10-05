namespace Bagatka.Sdk.GitHub;

/// <summary>A pull request to open.</summary>
/// <param name="Title">Its title.</param>
/// <param name="Head">The branch with the changes, in the same repository.</param>
/// <param name="Base">The branch to merge them into.</param>
/// <param name="Body">Its description, in Markdown.</param>
public sealed record GitHubNewPullRequest(string Title, string Head, string Base, string Body);
