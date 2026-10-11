using System;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// A sign-in in progress, until <paramref name="ExpiresAt"/>: the person opens <paramref name="Url"/>.
/// With a <paramref name="UserCode"/> (a Copilot plan, at GitHub; a ChatGPT plan, at OpenAI), they
/// enter it there and approve, while the caller completes the sign-in every <paramref name="Interval"/>
/// until it is done. Without one (a Claude plan), they approve there and bring back the code the page
/// shows, which the caller completes the sign-in with, once.
/// </summary>
/// <param name="Id">The sign-in.</param>
/// <param name="Url">The vendor's sign-in page.</param>
/// <param name="UserCode">What the person enters there, such as <c>WDJB-MJHT</c>, for a sign-in with a code.</param>
/// <param name="ExpiresAt">When the sign-in can no longer complete.</param>
/// <param name="Interval">How long to wait before completing a sign-in with a code; zero otherwise.</param>
public sealed record SignInStarted(SignInId Id, Uri Url, string? UserCode, DateTimeOffset ExpiresAt, TimeSpan Interval);
