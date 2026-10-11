using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Someone chose another model, thinking level, or account for the chat's agent, from its next turn on: an agent
/// working on a turn finishes it first, and the next agent continues the conversation.
/// </summary>
/// <param name="Account">The agent account that pays from now on.</param>
/// <param name="Model">The model from now on; <see langword="null"/> for the harness's default.</param>
/// <param name="Thinking">How hard the agent thinks from now on; <see langword="null"/> for the harness's default.</param>
/// <param name="ChangedBy">Who chose it.</param>
public sealed record ModelChanged(AgentAccountId Account, string? Model, string? Thinking, UserId ChangedBy);
