namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Input to <see cref="IChatsApi.SendAsync"/>.
/// </summary>
/// <param name="ChatId">The chat.</param>
/// <param name="Text">What to tell the agent: 1 to 100,000 characters.</param>
/// <param name="Proposal">The proposal this message sends on, as is or edited; only someone whose messages reach the agent may send one on.</param>
public sealed record SendMessage(ChatId ChatId, string Text, MessageId? Proposal);
