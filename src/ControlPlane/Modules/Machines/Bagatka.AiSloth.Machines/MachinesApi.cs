using System;
using Bagatka.AiSloth.Machines.Connections;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Machines.Data;
using Bagatka.AiSloth.Machines.Model;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Machines;

// The front door for both contracts: dependencies and the helpers several features share. Each
// feature is a file in Features/.
internal sealed partial class MachinesApi(
    MachinesDbContext db,
    IWorkspacesApi workspaces,
    MachineConnections connections,
    TimeProvider time) : IMachinesApi, IMachineConnectionsApi
{
    private MachineSummary Summary(Machine machine)
    {
        MachineStatus status = !machine.IsRegistered ? MachineStatus.AwaitingRegistration
            : connections.Find(machine.Id) is not null ? MachineStatus.Online
            : MachineStatus.Offline;
        return new MachineSummary(machine.Id, machine.WorkspaceId, machine.Name.Value, status, machine.AddedAt);
    }
}
