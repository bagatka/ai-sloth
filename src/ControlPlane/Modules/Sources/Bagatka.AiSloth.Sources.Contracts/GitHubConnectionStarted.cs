using System;

namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>
/// A GitHub connection waiting for the person: they open <paramref name="VerificationUri"/>, enter
/// <paramref name="UserCode"/>, and approve; meanwhile the caller completes it every
/// <paramref name="Interval"/> until it is connected or expires.
/// </summary>
/// <param name="Id">The attempt.</param>
/// <param name="UserCode">What the person enters at GitHub, such as <c>WDJB-MJHT</c>.</param>
/// <param name="VerificationUri">Where they enter it.</param>
/// <param name="ExpiresAt">When the code stops working.</param>
/// <param name="Interval">How long to wait between completions.</param>
public sealed record GitHubConnectionStarted(GitHubConnectionAttemptId Id, string UserCode, Uri VerificationUri, DateTimeOffset ExpiresAt, TimeSpan Interval);
