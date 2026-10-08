using System;
using System.Collections.Generic;
using System.Threading;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks.Daemons;

// Until when each nook is busy: now when a person reaches it or anything wakes it, or later when
// something keeps it busy, such as Chats while its agent works, or while a person watches one of its
// processes. It falls asleep once the sleep period has passed since. In this instance's memory, like
// daemon connections: after a restart every nook counts as used when first asked about.
internal sealed class NookActivity(NooksSettings settings, TimeProvider time)
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<NookId, DateTimeOffset> _busyUntil = [];
    private readonly Dictionary<NookId, int> _watchers = [];

    public void Used(NookId nookId)
    {
        KeepBusy(nookId, TimeSpan.Zero);
    }

    // Never shortens what was promised before.
    public void KeepBusy(NookId nookId, TimeSpan span)
    {
        DateTimeOffset until = time.GetUtcNow() + span;
        lock (_gate)
        {
            DateTimeOffset promised = _busyUntil.GetValueOrDefault(nookId);
            _busyUntil[nookId] = until > promised ? until : promised;
        }
    }

    // Someone watching the nook keeps it awake until they stop, and the sleep period starts then.
    public IDisposable Watching(NookId nookId)
    {
        lock (_gate)
        {
            _watchers[nookId] = _watchers.GetValueOrDefault(nookId) + 1;
        }

        return new Watch(this, nookId);
    }

    public bool Idle(NookId nookId)
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (_gate)
        {
            if (_watchers.ContainsKey(nookId))
            {
                return false;
            }

            bool known = _busyUntil.ContainsKey(nookId);
            if (!known)
            {
                _busyUntil[nookId] = now;
            }

            return _busyUntil[nookId] + settings.SleepAfter <= now;
        }
    }

    // Of these nooks, the one used least recently that nothing keeps busy now, if any.
    public NookId? IdlestOf(IReadOnlyList<NookId> nookIds)
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (_gate)
        {
            NookId? idlest = null;
            DateTimeOffset idlestSince = DateTimeOffset.MaxValue;
            foreach (NookId nookId in nookIds)
            {
                DateTimeOffset busyUntil = _busyUntil.GetValueOrDefault(nookId, now);
                bool busy = _watchers.ContainsKey(nookId) || busyUntil > now;
                if (!busy && busyUntil < idlestSince)
                {
                    idlest = nookId;
                    idlestSince = busyUntil;
                }
            }

            return idlest;
        }
    }

    // Ends the nook's sleep period now, so the lifecycle puts it to sleep on its next pass.
    public void SleepSoon(NookId nookId)
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (_gate)
        {
            _busyUntil[nookId] = now - settings.SleepAfter;
        }
    }

    // A deleted nook stays awake no more.
    public void Forget(NookId nookId)
    {
        lock (_gate)
        {
            _busyUntil.Remove(nookId);
        }
    }

    private void Unwatch(NookId nookId)
    {
        lock (_gate)
        {
            int left = _watchers.GetValueOrDefault(nookId) - 1;
            if (left > 0)
            {
                _watchers[nookId] = left;
            }
            else
            {
                _watchers.Remove(nookId);
            }
        }

        Used(nookId);
    }

    private sealed class Watch(NookActivity activity, NookId nookId) : IDisposable
    {
        private int _ended;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
            {
                activity.Unwatch(nookId);
            }
        }
    }
}
