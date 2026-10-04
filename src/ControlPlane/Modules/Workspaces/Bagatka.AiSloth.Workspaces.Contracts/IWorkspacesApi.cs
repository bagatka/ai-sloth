using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>
/// Workspaces: where people work together and what owns nooks, like a Slack workspace. A user
/// can be a member of many workspaces, such as a personal one and a company one.
/// </summary>
public interface IWorkspacesApi
{
    /// <summary>
    /// Creates a workspace. The actor, which must be a user, becomes its owner.
    /// </summary>
    /// <returns>The workspace; a validation error for an invalid name; or unauthorized for a non-user actor.</returns>
    public Task<Result<WorkspaceSummary>> CreateAsync(Actor actor, CreateWorkspace command, CancellationToken ct);

    /// <summary>
    /// The workspace as the actor sees it. Not found when it doesn't exist or the actor isn't a
    /// member, so non-members can't learn that it exists.
    /// </summary>
    public Task<Result<WorkspaceSummary>> GetAsync(Actor actor, WorkspaceId id, CancellationToken ct);

    /// <summary>
    /// The workspaces the actor, which must be a user, is a member of, oldest first.
    /// </summary>
    public Task<Result<Page<WorkspaceSummary>>> ListMineAsync(Actor actor, PageRequest page, CancellationToken ct);

    /// <summary>
    /// The actor's role in the workspace, or <see langword="null"/> when the actor isn't a member
    /// or the workspace doesn't exist. Other modules call it to apply their own permission rules.
    /// </summary>
    public Task<WorkspaceRole?> GetRoleAsync(Actor actor, WorkspaceId id, CancellationToken ct);
}
