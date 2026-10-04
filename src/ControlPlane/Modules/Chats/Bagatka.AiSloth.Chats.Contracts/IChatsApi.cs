using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Chats: conversations with a coding agent working in a nook. Every member of the nook's workspace
/// can read a chat; who may message the agent follows the agent account it runs on: every member
/// for the workspace's account, the owner and whoever they let in for a personal one. Each message
/// shows who sent it. The agent works only inside its nook, and acts there without asking.
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
    /// the harness doesn't take; or not found when the nook doesn't exist or the actor isn't a member of its workspace.
    /// </returns>
    public Task<Result<ChatSummary>> StartAsync(Actor actor, StartChat command, CancellationToken ct);

    /// <summary>The chat. Not found when it doesn't exist or the actor isn't a member of its workspace.</summary>
    public Task<Result<ChatSummary>> GetAsync(Actor actor, ChatId id, CancellationToken ct);

    /// <summary>The nook's chats, newest first. Not found when the actor may not use the nook.</summary>
    public Task<Result<Page<ChatSummary>>> ListAsync(Actor actor, NookId nookId, PageRequest page, CancellationToken ct);

    /// <summary>
    /// Sends a message to the agent. A message is never refused because the agent is working: it goes
    /// into the running turn when the agent supports that, and otherwise waits and starts the next
    /// turn. <see cref="WatchAsync"/> shows which happened.
    /// </summary>
    /// <returns>The message; a validation error for an empty or too long text; forbidden for a member who isn't one of the chat's senders; or not found.</returns>
    public Task<Result<ChatMessage>> SendAsync(Actor actor, SendMessage command, CancellationToken ct);

    /// <summary>
    /// Lets members besides a personal account's owner message a chat running on it, replacing whom
    /// it let in before. Only the account's owner may, and only when the account is shareable.
    /// </summary>
    /// <returns>Success; a validation error when the chat runs on the workspace's account, or the account isn't shareable; forbidden for anyone but its owner; or not found.</returns>
    public Task<Result> SetSendersAsync(Actor actor, SetChatSenders command, CancellationToken ct);

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
