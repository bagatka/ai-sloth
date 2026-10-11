using Bagatka.Foundation;
using Bagatka.ModelAccess;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// What running an agent on an account takes (<see cref="IAgentAccountsApi.UseAsync"/>): the endpoint
/// the model gateway forwards its calls to, with the headers that pay for them. Never log it, store it,
/// or return it to a client.
/// </summary>
/// <param name="Id">The account.</param>
/// <param name="Kind">What it is at its vendor.</param>
/// <param name="OwnerId">The person it belongs to, for a personal account.</param>
/// <param name="Endpoint">The endpoint and its headers.</param>
public sealed record AgentAccountCredential(AgentAccountId Id, ModelAccessKind Kind, UserId? OwnerId, ModelEndpoint Endpoint);
