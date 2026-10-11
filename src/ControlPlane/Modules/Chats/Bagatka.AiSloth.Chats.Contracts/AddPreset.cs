using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>Input to <see cref="IChatsApi.AddPresetAsync"/>.</summary>
/// <param name="WorkspaceId">A workspace whose chats it can start: its account is the workspace's, or the actor's own.</param>
/// <param name="Harness">The harness, as for <see cref="StartChat"/>.</param>
/// <param name="Account">The agent account, as for <see cref="StartChat"/>.</param>
/// <param name="Model">The model, as for <see cref="StartChat"/>.</param>
/// <param name="Thinking">How hard it thinks, as for <see cref="StartChat"/>.</param>
/// <param name="Name">
/// Its name for people, 1 to 64 characters; <see langword="null"/> for the harness's name and the
/// model's ID, such as <c>pi · openai/gpt-6-luna</c>.
/// </param>
public sealed record AddPreset(WorkspaceId WorkspaceId, string Harness, AgentAccountId Account, string Model, string Thinking, string? Name);
