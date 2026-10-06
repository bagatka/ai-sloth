using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Nooks.Jobs;

// A job's work on each nook, each running on its own, so a slow or hung provider, such as a machine
// pulling the nook image or one whose Docker Engine hangs, delays only the nooks it serves. A nook has
// one piece of work at a time, at most `capacity` run at once, and each is cancelled after `deadline`
// and tried again by a later pass. The job that makes it starts work from its passes and waits for
// what still runs as it stops, its stopping token having cancelled that work.
internal sealed class NookWork(int capacity, TimeSpan deadline, TimeProvider time, ILogger logger)
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<NookId, Task> _running = [];

    // The nooks with work running, for a pass to leave out.
    public List<NookId> Busy()
    {
        lock (_gate)
        {
            return [.. _running.Keys];
        }
    }

    // Starts the work unless the nook has work running or the job has no room left.
    public void TryStart(NookId nookId, Func<CancellationToken, Task> work, CancellationToken stoppingToken)
    {
        lock (_gate)
        {
            if (_running.Count < capacity && !_running.ContainsKey(nookId))
            {
                _running.Add(nookId, RunAsync(nookId, work, stoppingToken));
            }
        }
    }

    public async Task StoppedAsync()
    {
        Task[] running;
        lock (_gate)
        {
            running = [.. _running.Values];
        }

        try
        {
            await Task.WhenAll(running);
        }
        catch (OperationCanceledException)
        {
            // The job stopped, which cancelled them.
        }
    }

    private async Task RunAsync(NookId nookId, Func<CancellationToken, Task> work, CancellationToken stoppingToken)
    {
        // Returns to TryStart at once, so the work never runs under its lock and is recorded before it ends.
        await Task.Yield();
        try
        {
            using CancellationTokenSource late = new CancellationTokenSource(deadline, time);
            using CancellationTokenSource ending = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, late.Token);
            try
            {
                await work(ending.Token);
            }
            catch (OperationCanceledException) when (late.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
            {
                Log.NookWorkTooLong(logger, nookId.Value, deadline);
            }
        }
        finally
        {
            lock (_gate)
            {
                _running.Remove(nookId);
            }
        }
    }
}
