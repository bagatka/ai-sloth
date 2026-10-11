using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// What people keep for their chats (<see cref="IChatsApi"/>): presets, a person's saved choices of
/// harness, account, model, and thinking level; the instructions every agent gets, a workspace's and a
/// person's own; and the harness state agents keep between chats.
/// </summary>
public interface IChatSettingsApi
{
    /// <summary>
    /// The actor's presets that can start chats in the workspace, oldest first: those on the
    /// workspace's accounts, for people with Write on it, and those on their own accounts. A preset on
    /// an account that was removed isn't listed.
    /// </summary>
    /// <returns>The presets; forbidden for anyone but a person; or not found when the actor has no access to the workspace.</returns>
    public Task<Result<IReadOnlyList<PresetSummary>>> ListPresetsAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// Saves a preset of the actor's, checked as a chat's start is: a harness, an account the actor may
    /// use in the workspace of a kind the harness takes, and a model and thinking level only for a
    /// harness that takes them. A person keeps at most 20, each harness, account, model, and thinking
    /// level once.
    /// </summary>
    /// <returns>
    /// The preset; a validation error for its harness, account, model, or name; <see cref="ChatsErrors.PresetExists"/>;
    /// <see cref="ChatsErrors.TooManyPresets"/>; forbidden for anyone but a person; or not found when
    /// the actor has no access to the workspace.
    /// </returns>
    public Task<Result<PresetSummary>> AddPresetAsync(Actor actor, AddPreset command, CancellationToken ct);

    /// <summary>Renames one of the actor's presets: 1 to 64 characters.</summary>
    /// <returns>The preset; a validation error for the name; or <see cref="ChatsErrors.PresetNotFound"/>.</returns>
    public Task<Result<PresetSummary>> RenamePresetAsync(Actor actor, PresetId id, string name, CancellationToken ct);

    /// <summary>Removes one of the actor's presets. Chats started from it keep running as they are.</summary>
    /// <returns>Success; or <see cref="ChatsErrors.PresetNotFound"/>.</returns>
    public Task<Result> RemovePresetAsync(Actor actor, PresetId id, CancellationToken ct);

    /// <summary>
    /// The harness state the actor's agents in the workspace get, for each harness that keeps any: what
    /// its agents write for themselves to use later, such as Claude Code's memory, saved after each
    /// turn that changed it and given to the next agents. It follows who may direct the agents: the
    /// chats on the actor's own accounts share theirs, and the chats on the workspace's accounts share
    /// the workspace's. It never leaves its workspace.
    /// </summary>
    /// <returns>The states; forbidden for anyone but a person; or not found without access to the workspace.</returns>
    public Task<Result<IReadOnlyList<HarnessStateSummary>>> ListHarnessStatesAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// Forgets the actor's state for the harness in the workspace, or the workspace's shared one, which
    /// takes Write on the workspace: the next agents start without it. A chat whose agent already has
    /// it saves it again when it changes it. Forgetting none succeeds.
    /// </summary>
    public Task<Result> ForgetHarnessStateAsync(Actor actor, ForgetHarnessState command, CancellationToken ct);

    /// <summary>
    /// The instructions the actor's agents in the workspace get, whatever their harness: the
    /// workspace's, for everyone's chats there, and the actor's own, for the chats they start in any
    /// workspace. Agents read them as their user's standing instructions, from when they start.
    /// </summary>
    /// <returns>The instructions; forbidden for anyone but a person; or not found without access to the workspace.</returns>
    public Task<Result<Instructions>> GetInstructionsAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// Sets the workspace's instructions, up to 10,000 characters of Markdown; empty for none. People
    /// with Write on the workspace may. Agents that start afterwards follow them; running ones keep
    /// theirs.
    /// </summary>
    public Task<Result> SetWorkspaceInstructionsAsync(Actor actor, WorkspaceId workspaceId, string text, CancellationToken ct);

    /// <summary>
    /// Sets the actor's own instructions, up to 10,000 characters of Markdown; empty for none. Agents
    /// of the chats they start that start afterwards follow them.
    /// </summary>
    public Task<Result> SetPersonalInstructionsAsync(Actor actor, string text, CancellationToken ct);
}
