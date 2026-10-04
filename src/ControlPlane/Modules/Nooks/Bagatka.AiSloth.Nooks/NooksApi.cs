using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Jobs;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Sandboxing;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

// The front door for both contracts: dependencies and the helpers several features share. Each
// feature is a file in Features/. Streams that outlive a call use `databases`, never `db`.
internal sealed partial class NooksApi(
    NooksDbContext db,
    IDbContextFactory<NooksDbContext> databases,
    IWorkspacesApi workspaces,
    IMachinesApi machines,
    IEnumerable<ISandboxProvider> providers,
    DaemonConnections daemons,
    NookReconciler reconciler,
    TimeProvider time) : INooksApi, INookDaemonsApi
{
    // How long a call waits for a nook's daemon, such as a new nook's first connection.
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);

    // The nook, if the actor may use it: every member of its workspace may.
    private async Task<Nook?> FindNookAsync(Actor actor, NookId id, CancellationToken ct)
    {
        Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == id, ct);
        return nook is not null && await workspaces.GetRoleAsync(actor, nook.WorkspaceId, ct) is not null ? nook : null;
    }

    // The process, if the actor may use its nook.
    private async Task<Result<Process>> FindProcessAsync(Actor actor, NookId nookId, ProcessId processId, CancellationToken ct)
    {
        if (await FindNookAsync(actor, nookId, ct) is null)
        {
            return new Result<Process>(NooksErrors.NotFound);
        }

        Process? process = await db.Processes.SingleOrDefaultAsync(found => found.Id == processId && found.NookId == nookId, ct);
        return process is null ? new Result<Process>(NooksErrors.ProcessNotFound) : new Result<Process>(process);
    }

    // The nook's daemon connection, waiting for it to dial in; null when the nook can't run processes.
    private async Task<DaemonConnection?> ConnectionAsync(NookId nookId, CancellationToken ct)
    {
        NookStatus status = await db.Nooks.Where(nook => nook.Id == nookId).Select(nook => nook.Status).SingleAsync(ct);
        return status is NookStatus.Failed or NookStatus.Deleting ? null : await daemons.WaitAsync(nookId, ReadyTimeout, ct);
    }
}
