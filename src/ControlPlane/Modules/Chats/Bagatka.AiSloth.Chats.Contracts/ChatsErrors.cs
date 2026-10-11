using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Errors callers of <see cref="IChatsApi"/> may branch on.
/// </summary>
public static class ChatsErrors
{
    /// <summary>The chat doesn't exist, or the actor may not learn that it exists.</summary>
    public static readonly Error NotFound = Error.NotFound("chats.not_found", "Chat not found.");

    /// <summary>The preset doesn't exist, or isn't the actor's.</summary>
    public static readonly Error PresetNotFound = Error.NotFound("chats.preset_not_found", "Preset not found.");

    /// <summary>The actor has a preset of the same harness, account, and model already.</summary>
    public static readonly Error PresetExists = Error.Conflict("chats.preset_exists", "You have this preset already.");

    /// <summary>The actor has as many presets as a person may: remove one first.</summary>
    public static readonly Error TooManyPresets = Error.Conflict("chats.too_many_presets", "You have 20 presets, as many as a person may; remove one first.");

}
