namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>
/// A workspace as one person with access sees it.
/// </summary>
/// <param name="Id">The workspace.</param>
/// <param name="Name">Its display name.</param>
/// <param name="Access">How much the caller may do in it.</param>
public sealed record WorkspaceSummary(WorkspaceId Id, string Name, AccessLevel Access);
