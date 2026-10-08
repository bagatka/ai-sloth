using Bagatka.AiSloth.Nooks.Data;
using System.Threading.Tasks;
using System.Threading;
using System;
using System.Collections.Generic;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation.Modules;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<ProcessSummary>> StartProcessAsync(Actor actor, StartProcess command, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Nook> nook = await FindNookToOperateAsync(db, actor, command.NookId, ct);
        if (nook.Failed)
        {
            return new Result<ProcessSummary>(nook.Error);
        }

        Result<Process> started = Process.Start(command, time);
        if (started.Failed)
        {
            return new Result<ProcessSummary>(started.Error);
        }

        Process process = started.Output;

        IReadOnlyDictionary<string, string> environment = await processes.EnvironmentAsync(nook.Output, command.Environment, ct);

        Result<DaemonConnection> ready = await ReadyAsync(db, actor, nook.Output, changes: true, ct);
        if (ready.Failed)
        {
            return new Result<ProcessSummary>(ready.Error);
        }

        DaemonConnection connection = ready.Output;

        db.Processes.Add(process);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<ProcessSummary>(saved.Error);
        }

        // Not handled: the connection ending between the commit and the send, which leaves the
        // process recorded as running. Handling it would mean comparing recorded processes with the
        // daemon's hello when it reconnects.
        bool sent = await connection.SendAsync(new DaemonInstruction(process.ToInstruction(environment, inputStreamed: false)), ct);
        if (!sent)
        {
            throw new InvalidOperationException("Nook " + command.NookId.Value + "'s daemon disconnected before process " + process.Id.Value + " was sent.");
        }

        return new Result<ProcessSummary>(process.ToSummary());
    }
}
