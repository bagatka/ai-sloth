using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
/// The agent runs as a process in the nook, started with the first message and kept running. Every
/// event is saved as it arrives, so a chat outlives control-plane restarts and anyone can replay it.
/// </remarks>
public interface IChatsApi
{
    /// <summary>The harnesses chats can run, and the agent accounts each takes.</summary>
    public Task<IReadOnlyList<HarnessSummary>> ListHarnessesAsync(Actor actor, CancellationToken ct);

    /// <summary>
    /// Starts a chat and creates the nook its agent works in, on the provider, carrying the harness;
    /// the agent runs on the account, and starts with the first message. Other people reach the chat
    /// through its nook's access.
    /// </summary>
    /// <returns>
    /// The chat, with its nook; a validation error for an unknown harness or provider, or an account the
    /// actor can't use or the harness doesn't take; forbidden without Write on the workspace; or not found
    /// when the actor has no access to it.
    /// </returns>
    public Task<Result<ChatSummary>> StartAsync(Actor actor, StartChat command, CancellationToken ct);

    /// <summary>The chat. Not found when it doesn't exist or the actor has no access to its nook.</summary>
    public Task<Result<ChatSummary>> GetAsync(Actor actor, ChatId id, CancellationToken ct);

    /// <summary>The workspace's chats, newest first. Not found for anyone without access to the workspace, such as a nook's guest.</summary>
    public Task<Result<Page<ChatSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, PageRequest page, CancellationToken ct);

    /// <summary>
    /// Sends a message; it needs Write on the chat's nook. The message reaches the agent when the
    /// sender may use the chat's account, and is otherwise a proposal that someone who may use it
    /// sends on. A message is never refused because the agent is
    /// working: it goes into the running turn when the agent supports that, and otherwise waits and
    /// starts the next turn. <see cref="WatchAsync"/> shows which happened.
    /// </summary>
    /// <remarks>
    /// When the nook's disk is nearly full, the agent may fail to write and checkpoints may fail, so a
    /// message for the agent is sent only once the sender confirms (<see cref="SendMessage.ConfirmNearlyFullDisk"/>).
    /// </remarks>
    /// <returns>
    /// The message; <see cref="ChatsErrors.DiskNearlyFull"/>; a validation error for an empty or too long
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
    /// The actor's harness state in the workspace, for each harness that keeps any: what its agents
    /// write for themselves to use later, such as Claude Code's memory, saved from the chats the actor
    /// starts there after each turn that changed it, and given to the agents of their next chats
    /// there. It never leaves its workspace. A chat several people write in keeps the state of the
    /// person who started it.
    /// </summary>
    /// <returns>The states; forbidden for anyone but a person; or not found without access to the workspace.</returns>
    public Task<Result<IReadOnlyList<HarnessStateSummary>>> ListHarnessStatesAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// Forgets the actor's state for the harness in the workspace: agents of their chats there start
    /// without it. A chat whose agent already has it saves it again when it changes it. Forgetting
    /// none succeeds.
    /// </summary>
    public Task<Result> ForgetHarnessStateAsync(Actor actor, WorkspaceId workspaceId, string harness, CancellationToken ct);

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
    /// Cancelling <paramref name="ct"/> ends the watch, never the chat.
    /// </summary>
    public Task<Result<IAsyncEnumerable<ChatEvent>>> WatchAsync(Actor actor, WatchChat command, CancellationToken ct);
}
