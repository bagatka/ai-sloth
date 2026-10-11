using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Chats.Harness;

// One piece of work at a time in the background, for what it's about (its key), until it ends;
// stopping it cancels it and waits for it.
internal sealed class BackgroundWork<T>
    where T : class
{
    private object? _key;
    private CancellationTokenSource? _stop;
    private Task<T>? _done;

    // Its result once it ended; until then null, working on it from now on, instead of anything else.
    public async Task<T?> ResultAsync(object key, Func<CancellationToken, Task<T>> work, CancellationToken ct)
    {
        if (_done is null || !Equals(_key, key))
        {
            await StopAsync();
            _key = key;
            _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _done = work(_stop.Token);
        }

        if (!_done.IsCompletedSuccessfully)
        {
            return null;
        }

        return await _done;
    }

    public async Task StopAsync()
    {
        if (_stop is null || _done is null)
        {
            return;
        }

        await _stop.CancelAsync();
        try
        {
            await _done;
        }
        catch (OperationCanceledException)
        {
            // Stopped, as asked.
        }

        _stop.Dispose();
        _stop = null;
        _done = null;
        _key = null;
    }
}
