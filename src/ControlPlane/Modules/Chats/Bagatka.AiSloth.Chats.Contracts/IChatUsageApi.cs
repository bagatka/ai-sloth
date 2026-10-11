using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// What chats used of their models (<see cref="IChatsApi"/>), from every call the model gateway saw: a
/// chat's, and a workspace's by day and by chat.
/// </summary>
public interface IChatUsageApi
{
    /// <summary>
    /// What the chat's agents have used of their models (<see cref="ChatUsage"/>), for whoever may read
    /// the chat. Reads the chat's call records and what's known of its model; asks no vendor.
    /// </summary>
    /// <returns>The usage; or not found for a chat the actor can't see.</returns>
    public Task<Result<ChatUsage>> GetUsageAsync(Actor actor, ChatId id, CancellationToken ct);

    /// <summary>
    /// What the workspace's chats used of their models over its last days (<see cref="WorkspaceUsage"/>),
    /// for anyone who sees its chats: each day's calls, tokens, and cost, each chat's, and all of them
    /// added up. Days are those of the time zone given, such as the person's own. Reads the call
    /// records; asks no vendor.
    /// </summary>
    /// <param name="actor">Who asks.</param>
    /// <param name="workspaceId">The workspace.</param>
    /// <param name="days">How many days, today included: 1 to 90.</param>
    /// <param name="timeZone">The IANA time zone whose days count, such as <c>Europe/Berlin</c>.</param>
    /// <param name="ct">Cancels the reading.</param>
    /// <returns>The usage; a validation error for days out of range or an unknown time zone; or not found.</returns>
    public Task<Result<WorkspaceUsage>> GetWorkspaceUsageAsync(Actor actor, WorkspaceId workspaceId, int days, string timeZone, CancellationToken ct);
}
