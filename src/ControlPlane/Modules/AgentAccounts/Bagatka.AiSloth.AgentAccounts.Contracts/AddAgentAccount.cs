using System;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// Input to <see cref="IAgentAccountsApi.AddAsync"/>.
/// </summary>
/// <param name="WorkspaceId">The workspace to add it to, or <see langword="null"/> for the actor's own account.</param>
/// <param name="Kind">What it is at its vendor.</param>
/// <param name="Name">Its name for people: 1 to 64 characters.</param>
/// <param name="Secret">The key or token: 1 to 4,096 characters.</param>
/// <param name="Endpoint">
/// For an API key, the API's base URL when it isn't the vendor's own, such as
/// <c>https://openrouter.ai/api/v1</c> for OpenAI's API at OpenRouter; <see langword="null"/> for the
/// vendor's. Other kinds take none.
/// </param>
public sealed record AddAgentAccount(WorkspaceId? WorkspaceId, AgentAccountKind Kind, string Name, string Secret, Uri? Endpoint);
