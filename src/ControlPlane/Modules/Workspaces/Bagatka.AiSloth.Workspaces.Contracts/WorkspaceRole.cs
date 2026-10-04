namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>
/// What a member may do in a workspace. Each module decides what a role allows for its own data.
/// </summary>
public enum WorkspaceRole
{
    /// <summary>Manages the workspace and its members, and does everything a member does.</summary>
    Owner = 1,

    /// <summary>Uses the workspace: creates and works in its nooks.</summary>
    Member = 2,
}
