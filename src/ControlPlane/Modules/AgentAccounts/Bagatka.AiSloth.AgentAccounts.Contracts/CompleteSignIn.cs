
namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>Input to <see cref="IAgentAccountsApi.CompleteSignInAsync"/>.</summary>
/// <param name="Id">The sign-in.</param>
/// <param name="Code">
/// The code the vendor's page showed the person, for a sign-in without a user code (a Claude plan's,
/// <c>&lt;code&gt;#&lt;state&gt;</c>); none for a sign-in with a user code.
/// </param>
public sealed record CompleteSignIn(SignInId Id, string? Code);
