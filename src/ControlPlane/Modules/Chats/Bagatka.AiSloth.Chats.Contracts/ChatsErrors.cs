using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Errors callers of <see cref="IChatsApi"/> may branch on.
/// </summary>
public static class ChatsErrors
{
    /// <summary>The chat doesn't exist, or the actor may not learn that it exists.</summary>
    public static readonly Error NotFound = Error.NotFound("chats.not_found", "Chat not found.");

}
