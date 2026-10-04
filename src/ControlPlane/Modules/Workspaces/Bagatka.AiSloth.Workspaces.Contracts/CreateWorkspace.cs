namespace Bagatka.AiSloth.Workspaces.Contracts;

/// <summary>
/// Input to <see cref="IWorkspacesApi.CreateAsync"/>.
/// </summary>
/// <param name="Name">The display name, as the user typed it; the module validates it.</param>
public sealed record CreateWorkspace(string Name);
