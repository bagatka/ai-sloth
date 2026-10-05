using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// What running an agent on an account takes (<see cref="IAgentAccountsApi.UseAsync"/>): for most
/// kinds, the endpoint the model gateway forwards to with the headers that pay for the call; for a
/// plan whose token goes to its harness, the token. Never log it, store it, or return it to a client.
/// </summary>
/// <param name="Id">The account.</param>
/// <param name="Kind">What it is at its vendor.</param>
/// <param name="OwnerId">The person it belongs to, for a personal account.</param>
/// <param name="Access">The endpoint and its headers, or the token.</param>
public sealed record AgentAccountCredential(AgentAccountId Id, AgentAccountKind Kind, UserId? OwnerId, AgentAccountAccess Access);
