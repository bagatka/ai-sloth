using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// Machines: computers a workspace adds, such as a VPS or a Mac mini, so its nooks can run there as on
/// any provider. Nooks still run in containers, never on the machine's own operating system.
/// </summary>
public interface IMachinesApi
{
    /// <summary>
    /// Adds a machine to the workspace and returns the one-time code that registers it, valid for an
    /// hour. Only owners add machines.
    /// </summary>
    /// <returns>The machine and its code; a validation error for an invalid name; forbidden for a member who isn't an owner; or not found when the actor isn't a member.</returns>
    public Task<Result<MachineRegistration>> AddAsync(Actor actor, AddMachine command, CancellationToken ct);

    /// <summary>The workspace's machines, oldest first. Not found when the actor isn't a member.</summary>
    public Task<Result<IReadOnlyList<MachineSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>A machine. Not found when it doesn't exist or the actor isn't a member of its workspace.</summary>
    public Task<Result<MachineSummary>> GetAsync(Actor actor, MachineId id, CancellationToken ct);

    /// <summary>
    /// Removes a machine: its credential stops working at once and its connection ends. Nooks on it
    /// stop being reachable. Only owners remove machines.
    /// </summary>
    public Task<Result> RemoveAsync(Actor actor, MachineId id, CancellationToken ct);
}
