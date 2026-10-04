using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>
/// Errors callers of <see cref="IWorkspacesApi"/> may branch on.
/// </summary>
public static class WorkspacesErrors
{
    /// <summary>The workspace doesn't exist, or the actor may not learn that it exists.</summary>
    public static readonly Error NotFound = Error.NotFound("workspaces.not_found", "Workspace not found.");

    /// <summary>The workspace or nook doesn't exist, or the actor may not learn that it exists.</summary>
    public static readonly Error ResourceNotFound = Error.NotFound("workspaces.resource_not_found", "Not found.");

    /// <summary>The invite's code is unknown, was used, or expired.</summary>
    public static readonly Error InviteNotFound = Error.NotFound("workspaces.invite_not_found", "The invite doesn't exist, was used, or expired.");

    /// <summary>The person is the workspace's last manager; someone must keep managing it.</summary>
    public static readonly Error LastManager = Error.Conflict("workspaces.last_manager", "A workspace keeps at least one manager.");
}
