using System;

namespace Bagatka.Sdk.GitHub;

/// <summary>
/// One poll of a device flow sign-in: the tokens once the person approved, or GitHub's error:
/// <c>authorization_pending</c> and <c>slow_down</c> mean poll again after <paramref name="Interval"/>;
/// <c>expired_token</c>, <c>access_denied</c>, and others end the sign-in.
/// </summary>
/// <param name="Tokens">The tokens, once approved.</param>
/// <param name="Error">GitHub's error code, while not.</param>
/// <param name="Interval">The wait GitHub asks for before the next poll, after <c>slow_down</c>.</param>
public sealed record GitHubDevicePoll(GitHubUserTokens? Tokens, string? Error, TimeSpan? Interval);
