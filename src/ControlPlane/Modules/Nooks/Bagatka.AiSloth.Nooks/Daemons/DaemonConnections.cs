using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Nooks.Daemons;

// The daemon connections and output uploads this instance holds. Calls to a nook go through the
// instance its daemon dialed, so the active instance runs chats (ActiveInstance); when an instance
// hands over, its daemons are told to dial again, and reach the next one. Several instances working
// at once would need each nook's calls routed to the instance its daemon dialed.
internal sealed class DaemonConnections : IDisposable
{
    private readonly TimeProvider _time;
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<NookId, DaemonConnection> _connections = [];
    private readonly Dictionary<WatchId, OutputReceiver> _receivers = [];
    private readonly CancellationTokenRegistration _leaving;
    private TaskCompletionSource _connected = NewSignal();
    private bool _left;

    public DaemonConnections(ActiveInstance active, TimeProvider time)
    {
        _time = time;
        _leaving = active.Leaving.Register(MoveAll);
    }

    public void Dispose()
    {
        _leaving.Dispose();
    }

    // A newer connection for a nook replaces an older one, whose instruction stream ends. One that
    // reaches an instance that handed over is told to dial again at once.
    public DaemonConnection Connect(NookId nookId)
    {
        DaemonConnection connection = new DaemonConnection(nookId);
        DaemonConnection? replaced;
        lock (_gate)
        {
            if (_left)
            {
                connection.Move();
                return connection;
            }

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

    // Ends the nook's connection, such as when it fell asleep: its daemon is frozen or gone, and dials
    // in again when it wakes.
    public void Drop(NookId nookId)
    {
        DaemonConnection? dropped;
        lock (_gate)
        {
            dropped = _connections.GetValueOrDefault(nookId);
            _connections.Remove(nookId);
        }

        if (dropped is not null)
        {
            End(dropped);
        }
    }

    // Whether this instance handed over: its daemons dial the next one, and none comes back here.
    public bool Left
    {
        get
        {
            lock (_gate)
            {
                return _left;
            }
        }
    }

    // This instance handed over: every daemon dials again, and reaches the instance that is active.
    // Whoever waits for a daemon here stops waiting.
    private void MoveAll()
    {
        List<DaemonConnection> moving;
        TaskCompletionSource waiting;
        lock (_gate)
        {
            _left = true;
            moving = [.. _connections.Values];
            _connections.Clear();
            waiting = _connected;
        }

        waiting.TrySetResult();

        foreach (DaemonConnection connection in moving)
        {
            connection.Move();
            End(connection);
        }
    }

    public bool IsConnected(NookId nookId)
    {
        lock (_gate)
        {
            return _connections.ContainsKey(nookId);
        }
    }

    // The nook's connection, waiting up to `timeout` for its daemon to dial in; null if it doesn't, and
    // at once on an instance that handed over.
    public async Task<DaemonConnection?> WaitAsync(NookId nookId, TimeSpan timeout, CancellationToken ct)
    {
        long started = _time.GetTimestamp();
        while (true)
        {
            Task connected;
            lock (_gate)
            {
                if (_left)
                {
                    return null;
                }

                DaemonConnection? connection = _connections.GetValueOrDefault(nookId);
                if (connection is not null)
                {
                    return connection;
                }

                connected = _connected.Task;
            }

            TimeSpan left = timeout - _time.GetElapsedTime(started);
            if (left <= TimeSpan.Zero)
            {
                return null;
            }

            Task first = await Task.WhenAny(connected, Task.Delay(left, _time, ct));
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
