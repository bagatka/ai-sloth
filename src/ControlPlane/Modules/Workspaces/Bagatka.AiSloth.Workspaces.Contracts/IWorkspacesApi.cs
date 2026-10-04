using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>
/// Workspaces: where people work together and what owns nooks, like a Slack workspace, and who may
/// do what with them. People are given an <see cref="AccessLevel"/> on a <see cref="Resource"/>; access
/// to a resource reaches everything in it. Other modules ask <see cref="GetAccessAsync"/> and decide
/// what a level allows for their own data.
/// </summary>
public interface IWorkspacesApi
{
    /// <summary>
    /// Creates a workspace. The actor, which must be a user, manages it.
    /// </summary>
    /// <returns>The workspace; a validation error for an invalid name; or unauthorized for a non-user actor.</returns>
    public Task<Result<WorkspaceSummary>> CreateAsync(Actor actor, CreateWorkspace command, CancellationToken ct);

    /// <summary>
    /// The workspace as the actor sees it. Not found when it doesn't exist or the actor has no access
    /// to it, so others can't learn that it exists.
    /// </summary>
    public Task<Result<WorkspaceSummary>> GetAsync(Actor actor, WorkspaceId id, CancellationToken ct);

    /// <summary>
    /// The workspaces the actor, which must be a user, was given access to, oldest first. A nook's
    /// guest doesn't see the nook's workspace.
    /// </summary>
    public Task<Result<Page<WorkspaceSummary>>> ListMineAsync(Actor actor, PageRequest page, CancellationToken ct);

    /// <summary>
    /// The most the actor may do with the resource: the highest level given to them on it or on what
    /// it is in. <see langword="null"/> when they have no access, the resource is unknown, or the actor
    /// isn't a user. Two queries at most for a nook.
    /// </summary>
    public Task<AccessLevel?> GetAccessAsync(Actor actor, Resource resource, CancellationToken ct);

    /// <summary>
    /// Records that a new resource is in another, so access to the parent reaches it: a nook in its
    /// workspace. Call it before saving the resource; repeating it is fine. The actor needs Write on
    /// the parent.
    /// </summary>
    /// <returns>Success; a validation error for a workspace or a parent that isn't a workspace; not found or forbidden for the parent; or a conflict when the resource is already in another.</returns>
    public Task<Result> AddResourceAsync(Actor actor, AddResource command, CancellationToken ct);

    /// <summary>
    /// Creates a one-time invite to a resource, valid for 7 days. The actor needs Manage on it.
    /// </summary>
    /// <returns>The invite with its code; not found or forbidden for the resource.</returns>
    public Task<Result<Invite>> InviteAsync(Actor actor, CreateInvite command, CancellationToken ct);

    /// <summary>
    /// Gives the actor, which must be a user, the invite's access, keeping any higher level they had.
    /// The code then never works again.
    /// </summary>
    /// <returns>The resource; not found for an unknown, used, or expired code; a conflict when someone accepted it at the same moment; or unauthorized for a non-user actor.</returns>
    public Task<Result<Resource>> AcceptInviteAsync(Actor actor, AcceptInvite command, CancellationToken ct);

    /// <summary>
    /// Who was given access to the resource directly, by user ID. The actor needs Read on it.
    /// </summary>
    public Task<Result<IReadOnlyList<GrantSummary>>> ListGrantsAsync(Actor actor, Resource resource, CancellationToken ct);

    /// <summary>
    /// Ends someone's direct access to the resource; access through what it is in stays. The actor
    /// needs Manage on it. Ending access someone doesn't have succeeds.
    /// </summary>
    /// <returns>Success; not found or forbidden for the resource; or a conflict for a workspace's last manager.</returns>
    public Task<Result> RevokeAsync(Actor actor, RevokeAccess command, CancellationToken ct);
}
