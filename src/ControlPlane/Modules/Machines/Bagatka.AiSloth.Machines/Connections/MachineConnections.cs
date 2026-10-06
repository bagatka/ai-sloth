using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.Foundation.Modules;
using Bagatka.Sandboxing.Remote;

namespace Bagatka.AiSloth.Machines.Connections;

// The machines connected to this instance, each reached through a RemoteSandboxProvider that lasts as
// long as its connection. As for daemons, the active instance works with them (ActiveInstance); one
// that hands over ends its connections, and machines dial again, reaching the next one.
internal sealed class MachineConnections : IDisposable
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<MachineId, RemoteSandboxProvider> _connected = [];
    private readonly CancellationTokenRegistration _leaving;
    private bool _left;

    public MachineConnections(ActiveInstance active)
    {
        _leaving = active.Leaving.Register(DisconnectAll);
    }

    public void Dispose()
    {
        _leaving.Dispose();
    }

    // A newer connection replaces an older one, whose calls in flight fail. One that reaches an
    // instance that handed over ends at once, and its machine dials again.
    public RemoteSandboxProvider Connect(MachineId machine)
    {
        RemoteSandboxProvider connection = new RemoteSandboxProvider(MachineProvider.Name);
        RemoteSandboxProvider? replaced;
        lock (_gate)
        {
            if (_left)
            {
                connection.Disconnect();
                return connection;
            }

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

    // This instance handed over: every machine dials again, and reaches the instance that is active.
    private void DisconnectAll()
    {
        List<RemoteSandboxProvider> leaving;
        lock (_gate)
        {
            _left = true;
            leaving = [.. _connected.Values];
            _connected.Clear();
        }

        foreach (RemoteSandboxProvider connection in leaving)
        {
            connection.Disconnect();
        }
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
