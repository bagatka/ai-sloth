using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks.Daemons;

// The daemon connections and output uploads this instance holds. There is one active control-plane
// instance for now (ARCHITECTURE.md); several would need each nook's calls routed to the instance
// its daemon dialed.
internal sealed class DaemonConnections(TimeProvider time)
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<NookId, DaemonConnection> _connections = [];
    private readonly Dictionary<WatchId, OutputReceiver> _receivers = [];
    private TaskCompletionSource _connected = NewSignal();

    // A newer connection for a nook replaces an older one, whose instruction stream ends.
    public DaemonConnection Connect(NookId nookId)
    {
        DaemonConnection connection = new DaemonConnection(nookId);
        DaemonConnection? replaced;
        lock (_gate)
        {
            replaced = _connections.GetValueOrDefault(nookId);
            _connections[nookId] = connection;
            TaskCompletionSource connected = _connected;
            _connected = NewSignal();
            connected.TrySetResult();
        }

        if (replaced is not null)
        {
            End(replaced);
        }

        return connection;
    }

    public void Disconnect(DaemonConnection connection)
    {
        lock (_gate)
        {
            DaemonConnection? current = _connections.GetValueOrDefault(connection.NookId);
            if (current == connection)
            {
                _connections.Remove(connection.NookId);
            }
        }

        End(connection);
    }

    // The nook's connection, waiting up to `timeout` for its daemon to dial in; null if it doesn't.
    public async Task<DaemonConnection?> WaitAsync(NookId nookId, TimeSpan timeout, CancellationToken ct)
    {
        long started = time.GetTimestamp();
        while (true)
        {
            Task connected;
            lock (_gate)
            {
                DaemonConnection? connection = _connections.GetValueOrDefault(nookId);
                if (connection is not null)
                {
                    return connection;
                }

                connected = _connected.Task;
            }

            TimeSpan left = timeout - time.GetElapsedTime(started);
            if (left <= TimeSpan.Zero)
            {
                return null;
            }

            Task first = await Task.WhenAny(connected, Task.Delay(left, time, ct));
            if (first != connected)
            {
                ct.ThrowIfCancellationRequested();
                return null;
            }
        }
    }

    // Registers the receiver for an upload the caller is about to request on `connection`.
    public OutputReceiver Expect(DaemonConnection connection, ProcessId processId)
    {
        OutputReceiver receiver = new OutputReceiver(connection, processId);
        lock (_gate)
        {
            _receivers.Add(receiver.WatchId, receiver);
        }

        return receiver;
    }

    public OutputReceiver? Find(WatchId watchId)
    {
        lock (_gate)
        {
            return _receivers.GetValueOrDefault(watchId);
        }
    }

    public void Forget(OutputReceiver receiver)
    {
        lock (_gate)
        {
            _receivers.Remove(receiver.WatchId);
        }
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Uploads requested on an ended connection may never arrive, so their watches move on to the
    // next connection. One that is still arriving just stops counting.
    private void End(DaemonConnection connection)
    {
        connection.Close();
        List<OutputReceiver> requested;
        lock (_gate)
        {
            requested = _receivers.Values.Where(receiver => receiver.Connection == connection).ToList();
        }

        foreach (OutputReceiver receiver in requested)
        {
            receiver.End();
        }
    }
}
