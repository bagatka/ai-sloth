using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Threading;
using System;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<IAsyncEnumerable<ProcessEvent>>> WatchProcessAsync(Actor actor, WatchProcess command, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Process> found = await FindProcessAsync(db, actor, command.NookId, command.ProcessId, AccessLevel.Read, ct);
        if (found.Failed)
        {
            return new Result<IAsyncEnumerable<ProcessEvent>>(found.Error);
        }

        Result<DaemonConnection> connected = await lifecycle.ConnectAsync(db, actor, command.NookId, ct);
        if (connected.Failed)
        {
            return new Result<IAsyncEnumerable<ProcessEvent>>(connected.Error);
        }

        DaemonConnection connection = connected.Output;

        IAsyncEnumerable<ProcessEvent> events = processes.WatchAsync(connection, found.Output.Id, Math.Max(command.FromOffset, 0), ct);
        return new Result<IAsyncEnumerable<ProcessEvent>>(actor is UserActor ? KeptAwakeAsync(events, command.NookId, ct) : events);
    }

    // A person watching a process is using its nook, which stays awake while they watch. The control
    // plane's own watches, such as a chat's of its agent, don't count: an idle chat's nook sleeps.
    private async IAsyncEnumerable<ProcessEvent> KeptAwakeAsync(IAsyncEnumerable<ProcessEvent> events, NookId nookId, [EnumeratorCancellation] CancellationToken ct)
    {
        using IDisposable watching = activity.Watching(nookId);
        await foreach (ProcessEvent processEvent in events.WithCancellation(ct))
        {
            yield return processEvent;
        }
    }
}
