using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Chats.Harness;

// The reader of a chat's agent's output; disposing it stops the reader and waits for it.
internal sealed class OutputReading : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task _done;

    public OutputReading(Func<CancellationToken, Task> read, CancellationToken ct)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _done = read(_stop.Token);
    }

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _done;
        _stop.Dispose();
    }
}
