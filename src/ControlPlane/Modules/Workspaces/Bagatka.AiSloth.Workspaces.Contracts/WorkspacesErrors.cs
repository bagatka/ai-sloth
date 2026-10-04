using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>
/// Errors callers of <see cref="IWorkspacesApi"/> may branch on.
/// </summary>
public static class WorkspacesErrors
{
    /// <summary>The workspace doesn't exist, or the actor may not learn that it exists.</summary>
    public static readonly Error NotFound = Error.NotFound("workspaces.not_found", "Workspace not found.");
}
