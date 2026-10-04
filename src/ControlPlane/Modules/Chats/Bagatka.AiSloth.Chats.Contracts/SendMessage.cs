namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Input to <see cref="IChatsApi.SendAsync"/>.
/// </summary>
/// <param name="ChatId">The chat.</param>
/// <param name="Text">What to tell the agent: 1 to 100,000 characters.</param>
public sealed record SendMessage(ChatId ChatId, string Text);
