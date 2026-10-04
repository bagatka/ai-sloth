using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<IAsyncEnumerable<ProcessEvent>>> WatchProcessAsync(Actor actor, WatchProcess command, CancellationToken ct)
    {
        if (!(await FindProcessAsync(actor, command.NookId, command.ProcessId, ct)).TryGetValue(out Process? process, out Error? missing))
        {
            return new Result<IAsyncEnumerable<ProcessEvent>>(missing);
        }

        DaemonConnection? connection = await ConnectionAsync(command.NookId, ct);
        if (connection is null)
        {
            return new Result<IAsyncEnumerable<ProcessEvent>>(NooksErrors.NotReady);
        }

        return new Result<IAsyncEnumerable<ProcessEvent>>(WatchAsync(connection, process.Id, Math.Max(command.FromOffset, 0), ct));
    }

    // Relays the daemon's uploads. When the daemon's connection ends first, the watch asks again
    // from where it stopped on the next connection, so the watcher sees every byte once.
    private async IAsyncEnumerable<ProcessEvent> WatchAsync(DaemonConnection connection, ProcessId processId, long offset, [EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            using (OutputReceiver receiver = daemons.Expect(connection, processId))
            {
                try
                {
                    if (await connection.SendAsync(new DaemonInstruction(new WatchOutputInstruction(receiver.WatchId, processId, offset)), ct))
                    {
                        await foreach (ProcessOutput output in receiver.Output.ReadAllAsync(ct))
                        {
                            offset = output.Offset + output.Data.Length;
                            yield return new ProcessEvent(output);
                        }
                    }

                    if (receiver.Complete)
                    {
                        yield return new ProcessEvent(new ProcessExited(processId, await ExitCodeAsync(receiver, ct)));
                        yield break;
                    }
                }
                finally
                {
                    daemons.Forget(receiver);
                }
            }

            connection = await daemons.WaitAsync(connection.NookId, ReadyTimeout, ct)
                ?? throw new InvalidOperationException("Nook " + connection.NookId.Value + "'s daemon didn't reconnect in time; watch the process again later.");
        }
    }

    // Once all output is delivered, the exit is recorded already or reported within moments. Not
    // handled: a daemon that restarted lost its processes and never reports their exits; handling it
    // means marking them on its next hello.
    private async Task<int> ExitCodeAsync(OutputReceiver receiver, CancellationToken ct)
    {
        if (receiver.Exited.IsCompleted)
        {
            return await receiver.Exited;
        }

        await using NooksDbContext current = await databases.CreateDbContextAsync(ct);
        int? recorded = await current.Processes.Where(process => process.Id == receiver.ProcessId).Select(process => process.ExitCode).SingleAsync(ct);
        return recorded ?? await receiver.Exited.WaitAsync(ct);
    }
}
