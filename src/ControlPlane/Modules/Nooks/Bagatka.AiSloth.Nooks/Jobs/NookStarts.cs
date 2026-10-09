using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Jobs;

// When each nook began to start and how, until its daemon dials in, for product analytics: the start
// is then captured as nook_started with how long it took, and one that fails as nook_failed. How: new,
// ready_copy, woken, restored (from a checkpoint, after eviction), replaced (its sandbox was lost),
// recovered or resumed (its provider had stopped it). The first begin of a start counts, so later
// passes over it keep its time. Kept in memory, an entry per starting nook, gone when it starts,
// fails, or is deleted: a host that restarts meanwhile loses the measurement, never the start.
internal sealed class NookStarts(IProductEvents productEvents, TimeProvider time)
{
    private readonly ConcurrentDictionary<NookId, Start> _starting = new ConcurrentDictionary<NookId, Start>();

    public void Began(NookId nookId, string how, DateTimeOffset? since = null)
    {
        _starting.TryAdd(nookId, new Start(since ?? time.GetUtcNow(), how));
    }

    public void Ready(Nook nook)
    {
        bool starting = _starting.TryRemove(nook.Id, out Start? start);
        if (starting)
        {
            Capture("nook_started", nook, start!, withSeconds: true);
        }
    }

    public void Failed(Nook nook)
    {
        bool starting = _starting.TryRemove(nook.Id, out Start? start);
        if (starting)
        {
            Capture("nook_failed", nook, start!, withSeconds: false);
        }
    }

    public void Forget(NookId nookId)
    {
        _starting.TryRemove(nookId, out _);
    }

    // A nook nobody started, such as one only the host uses, belongs to no person and isn't captured.
    private void Capture(string name, Nook nook, Start start, bool withSeconds)
    {
        if ((nook.CreatedBy ?? nook.ReservedFor) is not UserId person)
        {
            return;
        }

        Dictionary<string, ProductFact> facts = new Dictionary<string, ProductFact>(StringComparer.Ordinal)
        {
            ["provider"] = new ProductFact(nook.Provider),
            ["how"] = new ProductFact(start.How),
        };
        if (withSeconds)
        {
            facts["seconds"] = new ProductFact((time.GetUtcNow() - start.At).TotalSeconds);
        }

        if (nook.Image is not null)
        {
            facts["image"] = new ProductFact(nook.Image);
        }

        productEvents.Capture(new ProductEvent(name, person, nook.WorkspaceId.Value, facts));
    }

    private sealed record Start(DateTimeOffset At, string How);
}
