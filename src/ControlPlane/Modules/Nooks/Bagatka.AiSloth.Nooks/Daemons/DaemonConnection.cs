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

    public NookId NookId { get; } = nookId;

    public ChannelReader<DaemonInstruction> Instructions => _instructions.Reader;

    // What the nook used at its daemon's latest report, kept only as long as the connection: a nook's
    // usage matters while it runs, and is measured again when it next does.
    public NookUsage? Usage { get; private set; }

    public void Reported(NookUsage usage)
    {
        Usage = usage;
    }

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
    }
}
