using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.Sandboxing.Remote;

namespace Bagatka.AiSloth.Machines.Connections;

// The machines connected to this instance, each reached through a RemoteSandboxProvider that lasts as
// long as its connection. One active control-plane instance for now, as for daemons.
internal sealed class MachineConnections
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<MachineId, RemoteSandboxProvider> _connected = [];

    // A newer connection replaces an older one, whose calls in flight fail.
    public RemoteSandboxProvider Connect(MachineId machine)
    {
        RemoteSandboxProvider connection = new RemoteSandboxProvider(MachineProvider.Name);
        RemoteSandboxProvider? replaced;
        lock (_gate)
        {
            replaced = _connected.GetValueOrDefault(machine);
            _connected[machine] = connection;
        }

        replaced?.Disconnect();
        return connection;
    }

    public void Disconnect(MachineId machine, RemoteSandboxProvider connection)
    {
        lock (_gate)
        {
            RemoteSandboxProvider? current = _connected.GetValueOrDefault(machine);
            if (current == connection)
            {
                _connected.Remove(machine);
            }
        }

        connection.Disconnect();
    }

    // The machine was removed: whatever connection it has ends.
    public void Disconnect(MachineId machine)
    {
        RemoteSandboxProvider? connection;
        lock (_gate)
        {
            _connected.Remove(machine, out connection);
        }

        connection?.Disconnect();
    }

    public RemoteSandboxProvider? Find(MachineId machine)
    {
        lock (_gate)
        {
            return _connected.GetValueOrDefault(machine);
        }
    }

    public IReadOnlyList<RemoteSandboxProvider> All()
    {
        lock (_gate)
        {
            return _connected.Values.ToList();
        }
    }
}
