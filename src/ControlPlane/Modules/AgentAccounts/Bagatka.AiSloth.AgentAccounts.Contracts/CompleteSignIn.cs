using System;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>Input to <see cref="IAgentAccountsApi.CompleteSignInAsync"/>.</summary>
/// <param name="Id">The sign-in.</param>
/// <param name="ReturnedTo">The whole address the person's browser returned to, query included.</param>
public sealed record CompleteSignIn(SignInId Id, Uri ReturnedTo);
