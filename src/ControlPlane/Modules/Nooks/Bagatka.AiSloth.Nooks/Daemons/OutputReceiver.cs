using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks.Daemons;

// The receiving end of one upload: what a daemon sends for one WatchOutputInstruction, the output and
// then the exit, relayed to the watch that asked for it. Disposing it means the watcher left, which
// ends the upload.
internal sealed class OutputReceiver : IDisposable
{
    // A small buffer: a slow watcher slows the upload, which slows the daemon's reading, never memory.
    private readonly Channel<ProcessEvent> _events = Channel.CreateBounded<ProcessEvent>(
        new BoundedChannelOptions(8) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

    private readonly CancellationTokenSource _watcherLeft = new CancellationTokenSource();

    public OutputReceiver(DaemonConnection connection, ProcessId processId)
    {
        Connection = connection;
        ProcessId = processId;
        WatcherLeft = _watcherLeft.Token;
    }

    public WatchId WatchId { get; } = WatchId.New();

    // The connection the WatchOutputInstruction went out on.
    public DaemonConnection Connection { get; }

    public ProcessId ProcessId { get; }

    public ChannelReader<ProcessEvent> Events => _events.Reader;

    public CancellationToken WatcherLeft { get; }

    // False when the watcher left.
    public async Task<bool> DeliverAsync(ProcessEvent processEvent, CancellationToken ct)
    {
        try
        {
            await _events.Writer.WriteAsync(processEvent, ct);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    // The upload ended, with or without the exit. Ending again changes nothing.
    public void End()
    {
        _events.Writer.TryComplete();
    }

    public void Dispose()
    {
        _watcherLeft.Cancel();
        _events.Writer.TryComplete();
        _watcherLeft.Dispose();
    }
}
