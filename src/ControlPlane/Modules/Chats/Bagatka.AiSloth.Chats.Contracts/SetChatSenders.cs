using System.Collections.Generic;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Input to <see cref="IChatsApi.SetSendersAsync"/>.
/// </summary>
/// <param name="ChatId">The chat.</param>
/// <param name="Members">The members besides the account's owner who may send messages, at most 100; empty for the owner alone.</param>
public sealed record SetChatSenders(ChatId ChatId, IReadOnlyList<UserId> Members);
