namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>
/// A workspace as one member sees it.
/// </summary>
/// <param name="Id">The workspace.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Role">The calling member's role in it.</param>
public sealed record WorkspaceSummary(WorkspaceId Id, string Name, WorkspaceRole Role);
