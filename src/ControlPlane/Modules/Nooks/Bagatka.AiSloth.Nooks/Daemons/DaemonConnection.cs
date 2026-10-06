using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks.Daemons;

// One daemon's control connection to this instance, while it lasts.
internal sealed class DaemonConnection(NookId nookId)
{
    // A daemon reads instructions as fast as they come; the bound only stops a stuck one from
    // growing memory.
    private readonly Channel<DaemonInstruction> _instructions = Channel.CreateBounded<DaemonInstruction>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    private readonly TaskCompletionSource _closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public NookId NookId { get; } = nookId;

    public ChannelReader<DaemonInstruction> Instructions => _instructions.Reader;

    // Completes when the connection ends.
    public Task Closed => _closed.Task;

    // False when the connection ended before the instruction could be queued.
    public async Task<bool> SendAsync(DaemonInstruction instruction, CancellationToken ct)
    {
        try
        {
            await _instructions.Writer.WriteAsync(instruction, ct);
            return true;
        }
        catch (ChannelClosedException)
        {
            return false;
        }
    }

    // Tells the daemon to dial again, which reaches the instance that is active now, then ends.
    public void Move()
    {
        _instructions.Writer.TryWrite(new DaemonInstruction(new ReconnectInstruction()));
        Close();
    }

    public void Close()
    {
        _instructions.Writer.TryComplete();
        _closed.TrySetResult();
    }
}
