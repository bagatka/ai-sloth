using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks.Daemons;

// The input feeds waiting for their daemons on this instance, by process, from the moment a process
// starts until its daemon takes the feed or the process ends.
internal sealed class InputFeeds
{
    private readonly Lock _gate = new Lock();
    private readonly Dictionary<ProcessId, InputFeed> _feeds = [];

    public InputFeed Register(NookId nookId, ProcessId processId, Func<Stream, CancellationToken, Task> write)
    {
        InputFeed feed = new InputFeed(nookId, processId, write);
        lock (_gate)
        {
            _feeds.Add(processId, feed);
        }

        return feed;
    }

    // The feed, once, for the daemon of the nook it is for.
    public InputFeed? Take(NookId nookId, ProcessId processId)
    {
        InputFeed? feed;
        lock (_gate)
        {
            feed = _feeds.GetValueOrDefault(processId);
            if (feed is null || feed.NookId != nookId)
            {
                return null;
            }

            _feeds.Remove(processId);
        }

        return feed.Take() ? feed : null;
    }

    // The process ended: a feed its daemon never took is abandoned.
    public void Forget(InputFeed feed)
    {
        lock (_gate)
        {
            _feeds.Remove(feed.ProcessId);
        }

        feed.Abandon();
    }
}
