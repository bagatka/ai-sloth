using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Errors callers of <see cref="IChatsApi"/> may branch on.
/// </summary>
public static class ChatsErrors
{
    /// <summary>The chat doesn't exist, or the actor may not learn that it exists.</summary>
    public static readonly Error NotFound = Error.NotFound("chats.not_found", "Chat not found.");

    /// <summary>The nook's disk is nearly full; send again confirming it, or free space first.</summary>
    public static readonly Error DiskNearlyFull = Error.Conflict(
        "chats.disk_nearly_full",
        "The nook's disk is nearly full: the agent may fail to write files, and checkpoints may fail. Free space, or send anyway.");
}
