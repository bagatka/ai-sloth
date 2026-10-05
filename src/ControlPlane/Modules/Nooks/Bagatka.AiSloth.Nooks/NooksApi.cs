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
using Bagatka.AiSloth.Secrets.Contracts;
using Bagatka.AiSloth.Sources.Contracts;
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
    ISecretsApi secrets,
    ISourcesApi sources,
    IEnumerable<ISandboxProvider> providers,
    DaemonConnections daemons,
    InputFeeds feeds,
    SourceLocks sourceLocks,
    NookReconciler reconciler,
    NooksSettings settings,
    TimeProvider time) : INooksApi, INookDaemonsApi
{
    // How long a call waits for a nook's daemon, such as a new nook's first connection.
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);

    // The nook, if the actor may do at least `needed` with it: a person as their access to it allows,
    // through its workspace or given to them directly, and the control plane's own processes, such as
    // Chats running a harness, anything. Not found when they may not see it, so nobody learns that it
    // exists; forbidden when they may see it but not do this.
    private async Task<Result<Nook>> FindNookAsync(Actor actor, NookId id, AccessLevel needed, CancellationToken ct)
    {
        Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == id, ct);
        if (nook is null)
        {
            return new Result<Nook>(NooksErrors.NotFound);
        }

        switch (actor)
        {
            case UserActor:
                AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Nook(id.Value), ct);
                if (access is null)
                {
                    return new Result<Nook>(NooksErrors.NotFound);
                }

                if (access < needed)
                {
                    return new Result<Nook>(Error.Forbidden);
                }

                return new Result<Nook>(nook);
            case SystemActor:
                return new Result<Nook>(nook);
            case AnonymousActor:
                return new Result<Nook>(NooksErrors.NotFound);
        }

        throw new InvalidOperationException("The actor has no kind.");
    }

    // The process, if the actor may do at least `needed` with its nook.
    private async Task<Result<Process>> FindProcessAsync(Actor actor, NookId nookId, ProcessId processId, AccessLevel needed, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(actor, nookId, needed, ct);
        if (nook.Failed)
        {
            return new Result<Process>(nook.Error);
        }

        Process? process = await db.Processes.SingleOrDefaultAsync(found => found.Id == processId && found.NookId == nookId, ct);
        return process is null ? new Result<Process>(NooksErrors.ProcessNotFound) : new Result<Process>(process);
    }

    // The nook's daemon connection, waiting for it to dial in; null when the nook can't run processes.
    private async Task<DaemonConnection?> ConnectionAsync(NookId nookId, CancellationToken ct)
    {
        NookStatus status = await db.Nooks.Where(nook => nook.Id == nookId).Select(nook => nook.Status).SingleAsync(ct);
        if (status is NookStatus.Failed or NookStatus.Deleting)
        {
            return null;
        }

        return await daemons.WaitAsync(nookId, ReadyTimeout, ct);
    }
}
