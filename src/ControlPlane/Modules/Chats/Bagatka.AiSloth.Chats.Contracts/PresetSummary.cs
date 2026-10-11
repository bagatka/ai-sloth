using System;
using Bagatka.AiSloth.AgentAccounts.Contracts;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// A person's saved choice of how a chat runs, a shortcut when they start one: a harness, the agent
/// account that pays, the model, and how hard it thinks.
/// </summary>
/// <param name="Id">The preset.</param>
/// <param name="Name">Its name for people, such as <c>pi · GPT-6 Luna</c>.</param>
/// <param name="Harness">The harness that runs the agent.</param>
/// <param name="Account">The agent account that pays for its work.</param>
/// <param name="Model">The model its agent runs on; <see langword="null"/> for the harness's default.</param>
/// <param name="Thinking">How hard its agent thinks; <see langword="null"/> for the harness's default.</param>
/// <param name="AddedAt">When it was saved.</param>
public sealed record PresetSummary(PresetId Id, string Name, string Harness, AgentAccountId Account, string? Model, string? Thinking, DateTimeOffset AddedAt);
