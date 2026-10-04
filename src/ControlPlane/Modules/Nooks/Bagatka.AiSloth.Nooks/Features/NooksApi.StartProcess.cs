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
        Nook? nook = await FindNookAsync(actor, command.NookId, ct);
        if (nook is null)
        {
            return new Result<ProcessSummary>(NooksErrors.NotFound);
        }

        Result<Process> started = Process.Start(command, time);
        if (started.Failed)
        {
            return new Result<ProcessSummary>(started.Error);
        }

        Process process = started.Output;

        DaemonConnection? connection = await ConnectionAsync(command.NookId, ct);
        if (connection is null)
        {
            return new Result<ProcessSummary>(NooksErrors.NotReady);
        }

        db.Processes.Add(process);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<ProcessSummary>(saved.Error);
        }

        // Not handled: the connection ending between the commit and the send, which leaves the
        // process recorded as running. Handling it would mean comparing recorded processes with the
        // daemon's hello when it reconnects.
        bool sent = await connection.SendAsync(new DaemonInstruction(process.ToInstruction(command.Environment)), ct);
        if (!sent)
        {
            throw new InvalidOperationException("Nook " + command.NookId.Value + "'s daemon disconnected before process " + process.Id.Value + " was sent.");
        }

        return new Result<ProcessSummary>(process.ToSummary());
    }
}
