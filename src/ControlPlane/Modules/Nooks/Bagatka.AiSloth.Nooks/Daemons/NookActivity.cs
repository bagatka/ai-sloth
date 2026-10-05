using System;
using System.Collections.Generic;
using System.Threading;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks.Daemons;

// Until when each nook stays awake: the sleep period after a person reaches it or anything wakes it,
// or longer when something keeps it awake, such as Chats while its agent works. In this instance's
// memory, like daemon connections: after a restart every nook counts as used when first asked about.
internal sealed class NookActivity(NooksSettings settings, TimeProvider time)
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<NookId, DateTimeOffset> _awakeUntil = [];

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

    public bool Idle(NookId nookId)
    {
        DateTimeOffset now = time.GetUtcNow();
        lock (_gate)
        {
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
}
