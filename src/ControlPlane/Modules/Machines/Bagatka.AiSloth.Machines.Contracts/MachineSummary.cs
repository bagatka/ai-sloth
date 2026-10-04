using System;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// A machine as the people with access to its workspace see it.
/// </summary>
/// <param name="Id">The machine.</param>
/// <param name="WorkspaceId">The workspace whose nooks it runs.</param>
/// <param name="Name">Its display name, such as <c>hetzner-1</c>.</param>
/// <param name="Status">Whether it can run nooks right now.</param>
/// <param name="AddedAt">When a manager added it.</param>
public sealed record MachineSummary(MachineId Id, WorkspaceId WorkspaceId, string Name, MachineStatus Status, DateTimeOffset AddedAt);
