using System;

namespace Bagatka.Sdk.GitHub;

/// <summary>A pull request.</summary>
/// <param name="Number">Its number in its repository.</param>
/// <param name="HtmlUrl">Its page.</param>
public sealed record GitHubPullRequest(int Number, Uri HtmlUrl);
