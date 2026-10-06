using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// A machine was removed from its workspace: its credential no longer works, and nothing runs there for
/// the workspace any more.
/// </summary>
/// <param name="MachineId">The machine, whose location named it.</param>
/// <param name="WorkspaceId">The workspace it served.</param>
public sealed record MachineRemoved(MachineId MachineId, WorkspaceId WorkspaceId);
