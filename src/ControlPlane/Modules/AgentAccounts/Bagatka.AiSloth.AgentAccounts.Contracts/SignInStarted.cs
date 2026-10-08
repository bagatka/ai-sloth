using System;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// A sign-in in progress: open <paramref name="Url"/> in the person's browser, then pass the address the
/// browser returns to on to complete the sign-in before <paramref name="ExpiresAt"/>.
/// </summary>
/// <param name="Id">The sign-in.</param>
/// <param name="Url">The vendor's sign-in page.</param>
/// <param name="ExpiresAt">When the sign-in can no longer complete.</param>
public sealed record SignInStarted(SignInId Id, Uri Url, DateTimeOffset ExpiresAt);
