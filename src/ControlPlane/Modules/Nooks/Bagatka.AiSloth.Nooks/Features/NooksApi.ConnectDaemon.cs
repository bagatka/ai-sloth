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

        return new Result<IAsyncEnumerable<DaemonInstruction>>(RelayAsync(nook.Id, reports, ct));
    }

    // The connection lasts while the caller reads its instructions.
    private async IAsyncEnumerable<DaemonInstruction> RelayAsync(NookId nookId, IAsyncEnumerable<DaemonReport> reports, [EnumeratorCancellation] CancellationToken ct)
    {
        DaemonConnection connection = daemons.Connect(nookId);
        using CancellationTokenSource ending = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task receiving = ReceiveReportsAsync(nookId, reports, ending.Token);
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

    private async Task ReceiveReportsAsync(NookId nookId, IAsyncEnumerable<DaemonReport> reports, CancellationToken ct)
    {
        try
        {
            await foreach (DaemonReport report in reports.WithCancellation(ct))
            {
                await using NooksDbContext current = await databases.CreateDbContextAsync(ct);
                switch (report)
                {
                    case ProcessExited exited:
                        Process? process = await current.Processes.SingleOrDefaultAsync(found => found.Id == exited.ProcessId && found.NookId == nookId, ct);
                        process?.Exited(exited.ExitCode, time);
                        await current.SaveAsync(ct);
                        break;
                    case DiskUsage disk:
                        Nook? nook = await current.Nooks.SingleOrDefaultAsync(found => found.Id == nookId, ct);
                        nook?.ReportDisk(disk);

                        // A conflict means the nook changed meanwhile; the next report, 30 seconds on, wins.
                        await current.SaveAsync(ct);
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
