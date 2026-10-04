using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks.Daemons;

// The receiving end of one upload: what a daemon sends for one WatchOutputInstruction, relayed to
// the watch that asked for it. Disposing it means the watcher left, which ends the upload.
internal sealed class OutputReceiver : IDisposable
{
    // A small buffer: a slow watcher slows the upload, which slows the daemon's reading, never memory.
    private readonly Channel<ProcessOutput> _output = Channel.CreateBounded<ProcessOutput>(
        new BoundedChannelOptions(8) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

    private readonly Lock _gate = new Lock();
    private readonly CancellationTokenSource _watcherLeft = new CancellationTokenSource();
    private bool _ended;
    private readonly TaskCompletionSource<int> _exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

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

    public ChannelReader<ProcessOutput> Output => _output.Reader;

    // True when the upload ended because the daemon delivered all of the process's output.
    public bool Complete { get; private set; }

    // The process's exit code, once the daemon reports it.
    public Task<int> Exited => _exited.Task;

    public CancellationToken WatcherLeft { get; }

    // False when the watcher left.
    public async Task<bool> DeliverAsync(ProcessOutput output, CancellationToken ct)
    {
        try
        {
            await _output.Writer.WriteAsync(output, ct);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    // The upload ended; `complete` says whether all the output arrived. Ending again changes nothing.
    // Complete is set before the output ends, so the watch reads it once it has read everything.
    public void End(bool complete)
    {
        lock (_gate)
        {
            if (_ended)
            {
                return;
            }

            _ended = true;
            Complete = complete;
        }

        _output.Writer.TryComplete();
    }

    public void ProcessExited(int exitCode)
    {
        _exited.TrySetResult(exitCode);
    }

    public void Dispose()
    {
        _watcherLeft.Cancel();
        _output.Writer.TryComplete();
        _watcherLeft.Dispose();
    }
}
