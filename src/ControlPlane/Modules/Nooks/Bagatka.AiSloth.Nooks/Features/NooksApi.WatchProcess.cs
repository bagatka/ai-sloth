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
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<IAsyncEnumerable<ProcessEvent>>> WatchProcessAsync(Actor actor, WatchProcess command, CancellationToken ct)
    {
        Result<Process> found = await FindProcessAsync(actor, command.NookId, command.ProcessId, AccessLevel.Read, ct);
        if (found.Failed)
        {
            return new Result<IAsyncEnumerable<ProcessEvent>>(found.Error);
        }

        DaemonConnection? connection = await ConnectionAsync(actor, command.NookId, ct);
        if (connection is null)
        {
            return new Result<IAsyncEnumerable<ProcessEvent>>(NooksErrors.NotReady);
        }

        return new Result<IAsyncEnumerable<ProcessEvent>>(WatchAsync(connection, found.Output.Id, Math.Max(command.FromOffset, 0), ct));
    }

    // Relays the daemon's uploads until one ends with the exit, which is recorded first, so whoever
    // learns of an exit here finds it recorded, as the daemon's own report may not be yet. An upload
    // that breaks off, because the daemon's connection ended, is asked for again from where it stopped
    // on the next connection, so the watcher sees every byte once.
    private async IAsyncEnumerable<ProcessEvent> WatchAsync(DaemonConnection connection, ProcessId processId, long offset, [EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            using (OutputReceiver receiver = daemons.Expect(connection, processId))
            {
                try
                {
                    bool asked = await connection.SendAsync(new DaemonInstruction(new WatchOutputInstruction(receiver.WatchId, processId, offset)), ct);
                    if (asked)
                    {
                        await foreach (ProcessEvent processEvent in receiver.Events.ReadAllAsync(ct))
                        {
                            if (processEvent.Value is ProcessExited exited)
                            {
                                await RecordExitAsync(connection.NookId, exited, ct);
                            }

                            yield return processEvent;
                            if (processEvent.Value is not ProcessOutput output)
                            {
                                yield break;
                            }

                            offset = output.Offset + output.Data.Length;
                        }
                    }
                }
                finally
                {
                    daemons.Forget(receiver);
                }
            }

            DaemonConnection? reconnected = await daemons.WaitAsync(connection.NookId, ReadyTimeout, ct);
            if (reconnected is null)
            {
                throw new InvalidOperationException("Nook " + connection.NookId.Value + "'s daemon didn't reconnect in time; watch the process again later.");
            }

            connection = reconnected;
        }
    }

    // Records a process's exit, from the daemon's report or a watch, whichever comes first.
    private async Task RecordExitAsync(NookId nookId, ProcessExited exited, CancellationToken ct)
    {
        await using NooksDbContext current = await databases.CreateDbContextAsync(ct);
        Process? process = await current.Processes.SingleOrDefaultAsync(found => found.Id == exited.ProcessId && found.NookId == nookId, ct);
        process?.Exited(exited.ExitCode, time);

        // A conflict means the other one recorded it.
        await current.SaveAsync(ct);
    }
}
