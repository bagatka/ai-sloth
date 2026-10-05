using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks.Daemons;

// One operation on a nook's files at a time on this instance, such as putting its sources in place
// or taking a checkpoint; whoever waits for a preparation finds it done. A nook's lock lives only
// while someone holds or waits for it.
internal sealed class FileLocks
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<NookId, Held> _held = [];

    public async Task<IDisposable> AcquireAsync(NookId nookId, CancellationToken ct)
    {
        Held held;
        lock (_gate)
        {
            held = _held.GetValueOrDefault(nookId) ?? new Held(this, nookId);
            _held[nookId] = held;
            held.Users++;
        }

        try
        {
            await held.Semaphore.WaitAsync(ct);
            return held;
        }
        catch (OperationCanceledException)
        {
            Leave(held);
            throw;
        }
    }

    private void Leave(Held held)
    {
        lock (_gate)
        {
            held.Users--;
            if (held.Users == 0)
            {
                _held.Remove(held.NookId);
                held.Semaphore.Dispose();
            }
        }
    }

    private sealed class Held(FileLocks locks, NookId nookId) : IDisposable
    {
        public NookId NookId { get; } = nookId;

        public SemaphoreSlim Semaphore { get; } = new SemaphoreSlim(1, 1);

        // Guarded by the locks' gate.
        public int Users { get; set; }

        public void Dispose()
        {
            Semaphore.Release();
            locks.Leave(this);
        }
    }
}
