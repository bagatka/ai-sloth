using Bagatka.AiSloth.Nooks.Data;
using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<NookSetup>> GetSetupAsync(Actor actor, NookId id, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Nook> nook = await FindNookAsync(db, actor, id, AccessLevel.Read, ct);
        if (nook.Failed)
        {
            return new Result<NookSetup>(nook.Error);
        }

        Result<DaemonConnection> ready = await ReadyAsync(db, actor, nook.Output, changes: false, ct);
        if (ready.Failed)
        {
            return new Result<NookSetup>(ready.Error);
        }

        DaemonConnection connection = ready.Output;

        NookSetup setup = await SetupOfAsync(db, nook.Output, ct);
        return new Result<NookSetup>(setup);
    }

    public async Task<Result<NookSetup>> RunSetupAsync(Actor actor, NookId id, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Nook> nook = await FindNookToOperateAsync(db, actor, id, ct);
        if (nook.Failed)
        {
            return new Result<NookSetup>(nook.Error);
        }

        Result<DaemonConnection> ready = await ReadyAsync(db, actor, nook.Output, changes: true, ct);
        if (ready.Failed)
        {
            return new Result<NookSetup>(ready.Error);
        }

        DaemonConnection connection = ready.Output;

        Result again = await files.RunSetupAgainAsync(db, nook.Output, connection, ct);
        if (again.Failed)
        {
            return new Result<NookSetup>(again.Error);
        }

        NookSetup setup = await SetupOfAsync(db, nook.Output, ct);
        return new Result<NookSetup>(setup);
    }

    private static async Task<NookSetup> SetupOfAsync(NooksDbContext db, Nook nook, CancellationToken ct)
    {
        if (nook.SetupProcessId is not ProcessId processId)
        {
            return new NookSetup(nook.SetupScripts, Run: null, nook.SetUpFromReadyCopyMadeAt);
        }

        Process? process = await db.Processes.AsNoTracking().SingleOrDefaultAsync(found => found.Id == processId, ct);
        if (process is null)
        {
            throw new InvalidOperationException("Nook " + nook.Id.Value + "'s setup process " + processId.Value + " isn't recorded.");
        }

        return new NookSetup(nook.SetupScripts, new SetupRun(process.Id, process.StartedAt, process.ExitedAt, process.ExitCode), nook.SetUpFromReadyCopyMadeAt);
    }
}
