using System;

namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>What a push did.</summary>
/// <param name="Branch">The branch pushed to.</param>
/// <param name="Commits">How many commits the branch has beyond its base.</param>
/// <param name="BranchUrl">The branch's page.</param>
/// <param name="PullRequestUrl">The pull request's page, when one was asked for.</param>
public sealed record PushedChanges(string Branch, int Commits, Uri BranchUrl, Uri? PullRequestUrl);
