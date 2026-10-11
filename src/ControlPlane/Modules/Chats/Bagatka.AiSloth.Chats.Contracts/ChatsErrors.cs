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

    /// <summary>The chat's setup didn't fail, so there is nothing to fix.</summary>
    public static readonly Error SetupNotFailed = Error.Conflict("chats.setup_not_failed", "This chat's setup didn't fail; there is nothing to fix.");

    /// <summary>No chat fixes the chat's setup: start one with <see cref="IChatsApi.FixSetupAsync"/>.</summary>
    public static readonly Error NoSetupFix = Error.Conflict("chats.no_setup_fix", "No chat fixes this chat's setup yet.");

    /// <summary>The chat that fixes the setup hasn't yet run it again and seen it work.</summary>
    public static readonly Error SetupNotFixed = Error.Conflict("chats.setup_not_fixed", "The other chat hasn't fixed the setup yet.");

    /// <summary>
    /// The fix doesn't apply to the chat's files, as when the chat changed the same lines: its agent can
    /// bring it in.
    /// </summary>
    public static readonly Error SetupFixDoesNotApply = Error.Conflict("chats.setup_fix_does_not_apply", "The fix doesn't apply to this chat's files, as when this chat changed the same lines; ask its agent to bring the fix in.");

}
