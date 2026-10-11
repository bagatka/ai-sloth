namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// Input to <see cref="IAgentAccountsApi.StartSignInAsync"/>: a plan added by signing in at its vendor,
/// always the actor's own.
/// </summary>
/// <param name="Kind">
/// What it is at its vendor: <see cref="AgentAccountKind.ChatGptPlan"/>,
/// <see cref="AgentAccountKind.CopilotPlan"/>, or <see cref="AgentAccountKind.ClaudePlan"/>.
/// </param>
/// <param name="Name">Its name for people: 1 to 64 characters.</param>
public sealed record StartSignIn(AgentAccountKind Kind, string Name);
