using System;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// How a sign-in stands: the account, once added; until then, the person hasn't approved its code yet,
/// and the caller completes it again after <paramref name="RetryAfter"/>.
/// </summary>
/// <param name="Account">The account, once added.</param>
/// <param name="RetryAfter">How long to wait before completing it again, while there is no account.</param>
public sealed record SignInProgress(AgentAccountSummary? Account, TimeSpan RetryAfter);
