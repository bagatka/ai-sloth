using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Chats: conversations with a coding agent working in a nook. A chat is as open as its nook: Read
/// reads it, Write writes in it. A message reaches the agent when its sender may use the chat's
/// account, and is a proposal otherwise. Each message shows who sent it. The agent works only inside
/// its nook, and acts there without asking.
/// </summary>
/// <remarks>
/// The agent runs as a process in the nook, started with the first message and kept running. Every
/// event is saved as it arrives, so a chat outlives control-plane restarts and anyone can replay it.
/// </remarks>
public interface IChatsApi
{
    /// <summary>The harnesses chats can run, and the agent accounts each takes. A nook carries one, chosen when it is created.</summary>
    public Task<IReadOnlyList<HarnessSummary>> ListHarnessesAsync(Actor actor, CancellationToken ct);

    /// <summary>Starts a chat in the nook, run by the nook's harness on the account. The agent starts with the first message.</summary>
    /// <returns>
    /// The chat; a validation error for a nook without a harness, or an account the actor can't use or
    /// the harness doesn't take; forbidden without Write on the nook; or not found when the nook doesn't exist or the actor has no access to it.
    /// </returns>
    public Task<Result<ChatSummary>> StartAsync(Actor actor, StartChat command, CancellationToken ct);

    /// <summary>The chat. Not found when it doesn't exist or the actor has no access to its nook.</summary>
    public Task<Result<ChatSummary>> GetAsync(Actor actor, ChatId id, CancellationToken ct);

    /// <summary>The nook's chats, newest first. Not found when the actor may not use the nook.</summary>
    public Task<Result<Page<ChatSummary>>> ListAsync(Actor actor, NookId nookId, PageRequest page, CancellationToken ct);

    /// <summary>
    /// Sends a message; it needs Write on the chat's nook. The message reaches the agent when the
    /// sender may use the chat's account, and is otherwise a proposal that someone who may use it
    /// sends on. A message is never refused because the agent is
    /// working: it goes into the running turn when the agent supports that, and otherwise waits and
    /// starts the next turn. <see cref="WatchAsync"/> shows which happened.
    /// </summary>
    /// <returns>The message; a validation error for an empty or too long text, or a proposal not in the chat; forbidden when sending on a proposal from someone whose messages don't reach the agent; or not found.</returns>
    public Task<Result<ChatMessage>> SendAsync(Actor actor, SendMessage command, CancellationToken ct);

    /// <summary>
    /// Stops the agent: the running turn ends as <c>cancelled</c>, and messages it hasn't received yet
    /// are cancelled. Stopping an idle chat succeeds. The agent's process keeps running for the next
    /// message.
    /// </summary>
    public Task<Result> StopAsync(Actor actor, ChatId id, CancellationToken ct);

    /// <summary>
    /// Streams the chat's events after a sequence number, first those already saved and then live.
    /// Cancelling <paramref name="ct"/> ends the watch, never the chat.
    /// </summary>
    public Task<Result<IAsyncEnumerable<ChatEvent>>> WatchAsync(Actor actor, WatchChat command, CancellationToken ct);
}
