using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Model;

// A member a personal account's owner let send messages to a chat running on it.
internal sealed class ChatSender(ChatId chatId, UserId userId)
{
    public const int MaxPerChat = 100;

    public ChatId ChatId { get; private set; } = chatId;

    public UserId UserId { get; private set; } = userId;
}
