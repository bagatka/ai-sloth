using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Jobs;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Sandboxing;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

// The front door for both contracts: dependencies and the helpers several features share. Each
// feature is a file in Features/.
internal sealed partial class NooksApi(
    IDbContextFactory<NooksDbContext> databases,
    IWorkspacesApi workspaces,
    IMachinesApi machines,
    ISourcesApi sources,
    IEnumerable<ISandboxProvider> providers,
    DaemonConnections daemons,
    InputFeeds feeds,
    NookProcesses processes,
    NookFiles files,
    Checkpoints checkpoints,
    KeptFolders folders,
    NookLifecycle lifecycle,
    NookActivity activity,
    NookStarts starts,
    NooksSettings settings,
    TimeProvider time) : INooksApi, INookDaemonsApi
{
    // The nook, if the actor may do at least `needed` with it: a person as their access to it allows,
    // through its workspace or given to them directly, and the control plane's own processes, such as
    // Chats running a harness, anything. Not found when they may not see it, so nobody learns that it
    // exists; forbidden when they may see it but not do this.
    private async Task<Result<Nook>> FindNookAsync(NooksDbContext db, Actor actor, NookId id, AccessLevel needed, CancellationToken ct)
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

    // The nook, if the actor may operate it: change what runs in it or its files. The person it is
    // reserved for alone, when it is; otherwise anyone with Write on its workspace, not people invited
    // to the nook alone, as whoever operates a nook can use what its agent can.
    private async Task<Result<Nook>> FindNookToOperateAsync(NooksDbContext db, Actor actor, NookId id, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(db, actor, id, AccessLevel.Write, ct);
        if (nook.Failed || actor is not UserActor user)
        {
            return nook;
        }

        if (nook.Output.ReservedFor is UserId only)
        {
            return only == user.UserId ? nook : new Result<Nook>(NooksErrors.Reserved);
        }

        bool member = await WritesInWorkspaceAsync(actor, nook.Output, ct);
        return member ? nook : new Result<Nook>(Error.Forbidden);
    }

    private async Task<bool> WritesInWorkspaceAsync(Actor actor, Nook nook, CancellationToken ct)
    {
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(nook.WorkspaceId), ct);
        return access >= AccessLevel.Write;
    }

    private async Task<Result<Process>> FindProcessToOperateAsync(NooksDbContext db, Actor actor, NookId nookId, ProcessId processId, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookToOperateAsync(db, actor, nookId, ct);
        if (nook.Failed)
        {
            return new Result<Process>(nook.Error);
        }

        Process? process = await db.Processes.SingleOrDefaultAsync(found => found.Id == processId && found.NookId == nookId, ct);
        return process is null ? new Result<Process>(NooksErrors.ProcessNotFound) : new Result<Process>(process);
    }

    // The process, if the actor may do at least `needed` with its nook.
    private async Task<Result<Process>> FindProcessAsync(NooksDbContext db, Actor actor, NookId nookId, ProcessId processId, AccessLevel needed, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(db, actor, nookId, needed, ct);
        if (nook.Failed)
        {
            return new Result<Process>(nook.Error);
        }

        Process? process = await db.Processes.SingleOrDefaultAsync(found => found.Id == processId && found.NookId == nookId, ct);
        return process is null ? new Result<Process>(NooksErrors.ProcessNotFound) : new Result<Process>(process);
    }

    // The nook's daemon connection once its files can be worked with: woken when it sleeps, its files
    // put in place and its setup started the first time, its resume scripts started after it woke.
    // An operation that `changes` the nook takes its ready copy first, when its setup earned one.
    private async Task<Result<DaemonConnection>> ReadyAsync(NooksDbContext db, Actor actor, Nook nook, bool changes, CancellationToken ct)
    {
        Result<DaemonConnection> connected = await lifecycle.ConnectAsync(db, actor, nook.Id, ct);
        if (connected.Failed)
        {
            return new Result<DaemonConnection>(connected.Error);
        }

        DaemonConnection connection = connected.Output;

        Result prepared = await files.PrepareAsync(db, nook, connection, ct);
        if (prepared.Failed)
        {
            return new Result<DaemonConnection>(prepared.Error);
        }

        if (changes)
        {
            await files.KeepReadyCopyAsync(db, nook, ct);
        }

        return new Result<DaemonConnection>(connection);
    }

    // The nook as people see it, with what it uses while it runs.
    private NookSummary SummaryOf(Nook nook, IReadOnlyList<NookSource> copies)
    {
        return nook.ToSummary(copies, daemons.UsageOf(nook.Id), settings.NearlyFull);
    }
}
