using System;
using System.Collections.Generic;
using System.Threading;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks.Daemons;

// Until when each nook stays awake: the sleep period after a person reaches it or anything wakes it,
// or longer when something keeps it awake, such as Chats while its agent works or a person watching
// one of its processes. In this instance's memory, like daemon connections: after a restart every
// nook counts as used when first asked about.
internal sealed class NookActivity(NooksSettings settings, TimeProvider time)
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<NookId, DateTimeOffset> _awakeUntil = [];
    private readonly Dictionary<NookId, int> _watchers = [];

    public void Used(NookId nookId)
    {
        KeepAwake(nookId, settings.SleepAfter);
    }

    // Never shortens what was promised before.
    public void KeepAwake(NookId nookId, TimeSpan span)
    {
        DateTimeOffset until = time.GetUtcNow() + span;
        lock (_gate)
        {
            DateTimeOffset promised = _awakeUntil.GetValueOrDefault(nookId);
            _awakeUntil[nookId] = until > promised ? until : promised;
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

            bool known = _awakeUntil.ContainsKey(nookId);
            if (!known)
            {
                _awakeUntil[nookId] = now + settings.SleepAfter;
            }

            return _awakeUntil[nookId] <= now;
        }
    }

    // A deleted nook stays awake no more.
    public void Forget(NookId nookId)
    {
        lock (_gate)
        {
            _awakeUntil.Remove(nookId);
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
