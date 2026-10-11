using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Chats: conversations with a coding agent working in a nook of its own. One chat, one nook, one
/// agent, so agents never work on each other's files. A chat is as open as its nook: Read reads it,
/// Write writes in it. A message reaches the agent when its sender may use the chat's
/// account, and is a proposal otherwise. Each message shows who sent it. The agent works only inside
/// its nook, and acts there without asking.
/// </summary>
/// <remarks>
/// The agent runs as a process in the nook, started with the chat once the nook's setup ended,
/// and kept running. Every
/// event is saved as it arrives, so a chat outlives control-plane restarts and anyone can replay it.
/// </remarks>
public interface IChatsApi
{
    /// <summary>The harnesses chats can run, and the agent accounts each takes.</summary>
    public Task<IReadOnlyList<HarnessSummary>> ListHarnessesAsync(Actor actor, CancellationToken ct);

    /// <summary>
    /// Starts a chat and creates the nook its agent works in, on the provider, carrying the harness;
    /// the agent runs on the account, and starts now, once the nook's setup ended, while people
    /// write. Other people reach the chat through its nook's access.
    /// </summary>
    /// <remarks>
    /// Apps start a chat as someone starts writing its first message, so it is ready when they send it.
    /// Until its first message the chat is a draft: it isn't listed, it goes with its nook after the
    /// draft lifetime (30 minutes unless the host says otherwise), and a person keeps at most two in a
    /// workspace, the oldest going when they start another.
    /// </remarks>
    /// <returns>
    /// The chat, with its nook; a validation error for an unknown harness or provider, or an account the
    /// actor can't use or the harness doesn't take; forbidden without Write on the workspace; or not found
    /// when the actor has no access to it.
    /// </returns>
    public Task<Result<ChatSummary>> StartAsync(Actor actor, StartChat command, CancellationToken ct);

    /// <summary>
    /// The models a chat's agent can run on with the harness and the account (<see cref="StartChat.Model"/>):
    /// those of the account's vendor that can call tools, newest first. Empty when the harness runs on
    /// its own default model, or the vendor's models aren't listed (<c>IAgentAccountsApi.ListModelsAsync</c>).
    /// Reads the vendor's catalog each time, which throws when it can't be reached; try again later.
    /// </summary>
    /// <returns>
    /// The models; a validation error for an unknown harness, or an account the actor can't use or the
    /// harness doesn't take; or not found when the actor has no access to the workspace.
    /// </returns>
    public Task<Result<IReadOnlyList<AgentModel>>> ListModelsAsync(Actor actor, WorkspaceId workspaceId, string harness, AgentAccountId account, CancellationToken ct);

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
    /// Runs the chat's agent on another model, at another thinking level, or on another account, from its
    /// next turn: an agent
    /// working on a turn finishes it, and a new agent on the same harness continues the conversation.
    /// The account must be one the actor may use, of a kind the harness takes, and whose the chat's
    /// is: the workspace's for a chat on the workspace's account, the same person's own for a chat on
    /// theirs. Choosing what the chat runs on already changes nothing.
    /// </summary>
    /// <returns>
    /// The chat as it runs from its next turn; a validation error for the account or the model;
    /// forbidden for anyone who may only read it; or not found.
    /// </returns>
    public Task<Result<ChatSummary>> ChangeModelAsync(Actor actor, ChangeModel command, CancellationToken ct);

    /// <summary>The chat. Not found when it doesn't exist or the actor has no access to its nook.</summary>
    public Task<Result<ChatSummary>> GetAsync(Actor actor, ChatId id, CancellationToken ct);

    /// <summary>The workspace's chats someone wrote in, newest first. Not found for anyone without access to the workspace, such as a nook's guest.</summary>
    public Task<Result<Page<ChatSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, PageRequest page, CancellationToken ct);

    /// <summary>
    /// Streams the workspace's chats as lists show them: first its newest 100 chats someone wrote in,
    /// newest first, then, each time some of those change, the ones that changed, such as a chat
    /// someone first wrote in, or one whose agent started or stopped working. Not found for anyone
    /// without access to the workspace.
    /// </summary>
    /// <remarks>
    /// Cancelling <paramref name="ct"/> ends the watch. It also ends when the actor loses access to the
    /// workspace, and when the control-plane instance serving it hands over, as in a deploy: watch
    /// again, which starts with the newest chats again. Not handled: older chats that change, which
    /// show in <see cref="ListAsync"/> only.
    /// </remarks>
    public Task<Result<IAsyncEnumerable<IReadOnlyList<ChatSummary>>>> WatchChatsAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// Sends a message; it needs Write on the chat's nook. The message reaches the agent when the
    /// sender may use the chat's account, and is otherwise a proposal that someone who may use it
    /// sends on. A message is never refused because the agent is
    /// working: it goes into the running turn when the agent supports that, and otherwise waits and
    /// starts the next turn. <see cref="WatchAsync"/> shows which happened.
    /// </summary>
    /// <returns>
    /// The message; a validation error for an empty or too long
    /// text, or a proposal not in the chat; forbidden when sending on a proposal from someone whose
    /// messages don't reach the agent; or not found.
    /// </returns>
    public Task<Result<ChatMessage>> SendAsync(Actor actor, SendMessage command, CancellationToken ct);


    /// <summary>
    /// Stops the agent: the running turn ends as <c>cancelled</c>, and messages it hasn't received yet
    /// are cancelled. Stopping an idle chat succeeds. The agent's process keeps running for the next
    /// message.
    /// </summary>
    public Task<Result> StopAsync(Actor actor, ChatId id, CancellationToken ct);

    /// <summary>
    /// Runs the chat's nook's setup again, as its scripts are now, such as after its agent fixed them:
    /// once the agent answered every message sent before, it stops, the setup runs, and an agent starts
    /// again with what the setup installed, continuing the conversation. The chat tells each step
    /// (<see cref="SetupRunAsked"/>, <see cref="SetupStarted"/>, <see cref="SetupEnded"/>). Asking again
    /// before it runs changes nothing.
    /// </summary>
    /// <returns>Nothing; forbidden for anyone who may only read the chat; or not found.</returns>
    public Task<Result> RunSetupAgainAsync(Actor actor, ChatId id, CancellationToken ct);

    /// <summary>
    /// Starts a chat that fixes the chat's failed setup, leaving the chat as it is: with the same
    /// repositories, harness, account, model, and thinking, in a new nook on the same provider, its
    /// agent is asked, as the actor, to fix the setup and commit the fix, then its setup runs again
    /// (<see cref="RunSetupAgainAsync"/>). Both chats tell it (<see cref="SetupFixStarted"/>). The fix
    /// reaches new nooks once it's pushed and merged.
    /// </summary>
    /// <returns>
    /// The chat that fixes it; <see cref="ChatsErrors.SetupNotFailed"/>; what starting a chat or
    /// sending a message refuses, such as an account the actor may not use; forbidden for anyone who
    /// may only read the chat; or not found.
    /// </returns>
    public Task<Result<ChatSummary>> FixSetupAsync(Actor actor, ChatId id, CancellationToken ct);

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

    /// <summary>
    /// Streams the chat's events after a sequence number, first those already saved and then live.
    /// Cancelling <paramref name="ct"/> ends the watch, never the chat. A watch also ends when the
    /// control-plane instance serving it hands over, as in a deploy: watch again after the last
    /// sequence number.
    /// </summary>
    public Task<Result<IAsyncEnumerable<ChatEvent>>> WatchAsync(Actor actor, WatchChat command, CancellationToken ct);
}
