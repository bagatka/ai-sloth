using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<IAsyncEnumerable<DaemonInstruction>>> ConnectAsync(Actor actor, ConnectDaemon command, IAsyncEnumerable<DaemonReport> reports, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        // A daemon proves its nook with the nook's token; the actor is always anonymous.
        Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == command.NookId, ct);
        if (nook is null || !nook.AcceptsDaemonToken(command.Token) || !nook.DaemonConnected())
        {
            return new Result<IAsyncEnumerable<DaemonInstruction>>(Error.Unauthorized);
        }

        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<IAsyncEnumerable<DaemonInstruction>>(saved.Error);
        }

        starts.Ready(nook);

        return new Result<IAsyncEnumerable<DaemonInstruction>>(RelayAsync(nook.Id, reports, ct));
    }

    // The connection lasts while the caller reads its instructions.
    private async IAsyncEnumerable<DaemonInstruction> RelayAsync(NookId nookId, IAsyncEnumerable<DaemonReport> reports, [EnumeratorCancellation] CancellationToken ct)
    {
        DaemonConnection connection = daemons.Connect(nookId);
        using CancellationTokenSource ending = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task receiving = ReceiveReportsAsync(connection, reports, ending.Token);
        try
        {
            await foreach (DaemonInstruction instruction in connection.Instructions.ReadAllAsync(ct))
            {
                yield return instruction;
            }
        }
        finally
        {
            daemons.Disconnect(connection);
            await ending.CancelAsync();
            await receiving;
        }
    }

    // A process's exit is recorded; what the nook uses is kept with the connection.
    // Not handled: processes a restarted daemon lost in the same sandbox never report an exit, so the
    // list shows them running; marking those its hello doesn't name as ended would.
    private async Task ReceiveReportsAsync(DaemonConnection connection, IAsyncEnumerable<DaemonReport> reports, CancellationToken ct)
    {
        try
        {
            await foreach (DaemonReport report in reports.WithCancellation(ct))
            {
                switch (report)
                {
                    case ProcessExited exited:
                        await processes.RecordExitAsync(connection.NookId, exited, ct);
                        break;
                    case NookUsage usage:
                        connection.Reported(usage);
                        break;
                }
            }
        }
        catch (System.OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The connection ended.
        }
    }
}
