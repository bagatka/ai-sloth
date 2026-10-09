using System;
using Bagatka.AiSloth.Machines.Connections;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Machines.Data;
using Bagatka.AiSloth.Machines.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Machines;

// The front door for both contracts: dependencies and the helpers several features share. Each
// feature is a file in Features/.
internal sealed partial class MachinesApi(
    IDbContextFactory<MachinesDbContext> databases,
    IWorkspacesApi workspaces,
    MachineConnections connections,
    IProductEvents productEvents,
    TimeProvider time,
    ILogger<MachinesApi> logger) : IMachinesApi, IMachineConnectionsApi
{
    private static readonly TimeSpan PingEvery = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PingWithin = TimeSpan.FromSeconds(15);

    private MachineSummary Summary(Machine machine)
    {
        bool connected = connections.Find(machine.Id) is not null;
        MachineStatus status = MachineStatus.AwaitingRegistration;
        if (machine.IsRegistered)
        {
            status = connected ? MachineStatus.Online : MachineStatus.Offline;
        }

        return new MachineSummary(machine.Id, machine.WorkspaceId, machine.Name.Value, status, machine.AddedAt);
    }
}
