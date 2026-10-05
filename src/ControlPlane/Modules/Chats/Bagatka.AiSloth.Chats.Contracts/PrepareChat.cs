namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Input to <see cref="IChatsApi.PrepareAsync"/>.
/// </summary>
/// <param name="ChatId">The chat whose project to prepare.</param>
/// <param name="ConfirmNearlyFullDisk">Whether to prepare it even when the nook's disk is nearly full.</param>
public sealed record PrepareChat(ChatId ChatId, bool ConfirmNearlyFullDisk);
