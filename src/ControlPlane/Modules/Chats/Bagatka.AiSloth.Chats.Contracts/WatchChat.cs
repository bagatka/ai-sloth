namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Input to <see cref="IChatsApi.WatchAsync"/>.
/// </summary>
/// <param name="ChatId">The chat.</param>
/// <param name="AfterSequence">The last sequence number already seen, or 0 for everything.</param>
public sealed record WatchChat(ChatId ChatId, long AfterSequence);
