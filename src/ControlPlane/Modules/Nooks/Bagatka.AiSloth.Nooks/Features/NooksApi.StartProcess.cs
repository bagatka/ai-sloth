using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<ProcessSummary>> StartProcessAsync(Actor actor, StartProcess command, CancellationToken ct)
    {
        if (await FindNookAsync(actor, command.NookId, ct) is null)
        {
            return new Result<ProcessSummary>(NooksErrors.NotFound);
        }

        if (!Process.Start(command, time).TryGetValue(out Process? process, out Error? invalid))
        {
            return new Result<ProcessSummary>(invalid);
        }

        DaemonConnection? connection = await ConnectionAsync(command.NookId, ct);
        if (connection is null)
        {
            return new Result<ProcessSummary>(NooksErrors.NotReady);
        }

        db.Processes.Add(process);
        if ((await db.SaveAsync(ct)).IsError(out Error? failed))
        {
            return new Result<ProcessSummary>(failed);
        }

        // Not handled: the connection ending between the commit and the send, which leaves the
        // process recorded as running. Handling it would mean comparing recorded processes with the
        // daemon's hello when it reconnects.
        if (!await connection.SendAsync(new DaemonInstruction(process.ToInstruction()), ct))
        {
            throw new InvalidOperationException("Nook " + command.NookId.Value + "'s daemon disconnected before process " + process.Id.Value + " was sent.");
        }

        return new Result<ProcessSummary>(process.ToSummary());
    }
}
