using Bagatka.AiSloth.AgentAccounts.Contracts;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>Input to <see cref="IChatsApi.ChangeModelAsync"/>.</summary>
/// <param name="ChatId">The chat.</param>
/// <param name="Account">The agent account that pays from now on, as for <see cref="StartChat"/>.</param>
/// <param name="Model">The model from now on, as for <see cref="StartChat"/>; <see langword="null"/> for the harness's default.</param>
public sealed record ChangeModel(ChatId ChatId, AgentAccountId Account, string? Model);
