using System;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// Input to <see cref="IAgentAccountsApi.StartSignInAsync"/>: an account added by signing in at its
/// vendor, always the actor's own.
/// </summary>
/// <param name="Kind">What it is at its vendor: <see cref="AgentAccountKind.ChatGptPlan"/>.</param>
/// <param name="Name">Its name for people: 1 to 64 characters.</param>
/// <param name="Callback">
/// Where the person's browser returns, which the caller listens on: for ChatGPT,
/// <c>http://127.0.0.1:&lt;port&gt;/auth/callback</c>.
/// </param>
public sealed record StartSignIn(AgentAccountKind Kind, string Name, Uri Callback);
