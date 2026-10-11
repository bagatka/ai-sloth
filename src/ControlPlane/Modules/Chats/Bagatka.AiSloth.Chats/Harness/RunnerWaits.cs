using System;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Chats.Harness;

// What a chat's runner waits for in the background, stopped when it stops: following the setup run its
// agent waits for, and a regular tick that has the runner look at its chat again.
internal sealed class RunnerWaits : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop;
    private readonly Task _ticking;

    public RunnerWaits(Action tick, TimeSpan every, TimeProvider time, CancellationToken ct)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ticking = TickAsync(tick, every, time, _stop.Token);
    }

    public BackgroundWork<SetupEnd> SetupRun { get; } = new BackgroundWork<SetupEnd>();

    public async ValueTask DisposeAsync()
    {
        await SetupRun.StopAsync();
        await _stop.CancelAsync();
        await _ticking;
        _stop.Dispose();
    }

    private static async Task TickAsync(Action tick, TimeSpan every, TimeProvider time, CancellationToken ct)
    {
        using PeriodicTimer timer = new PeriodicTimer(every, time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                tick();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The runner stopped.
        }
    }
}
