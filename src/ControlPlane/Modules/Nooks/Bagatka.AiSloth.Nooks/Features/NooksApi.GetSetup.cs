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
        Result<Nook> nook = await FindNookAsync(actor, id, AccessLevel.Read, ct);
        if (nook.Failed)
        {
            return new Result<NookSetup>(nook.Error);
        }

        DaemonConnection? connection = await ConnectionAsync(actor, id, ct);
        if (connection is null)
        {
            return new Result<NookSetup>(NooksErrors.NotReady);
        }

        // Putting the files in place starts the setup.
        Result prepared = await PrepareSourcesAsync(nook.Output, connection, ct);
        if (prepared.Failed)
        {
            return new Result<NookSetup>(prepared.Error);
        }

        NookSetup setup = await SetupOfAsync(nook.Output, ct);
        return new Result<NookSetup>(setup);
    }

    public async Task<Result<NookSetup>> RunSetupAsync(Actor actor, NookId id, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(actor, id, AccessLevel.Write, ct);
        if (nook.Failed)
        {
            return new Result<NookSetup>(nook.Error);
        }

        DaemonConnection? connection = await ConnectionAsync(actor, id, ct);
        if (connection is null)
        {
            return new Result<NookSetup>(NooksErrors.NotReady);
        }

        Result prepared = await PrepareSourcesAsync(nook.Output, connection, ct);
        if (prepared.Failed)
        {
            return new Result<NookSetup>(prepared.Error);
        }

        await KeepReadyCopyAsync(nook.Output, ct);

        // One run at a time: they share the log, and a second would race the first.
        using IDisposable held = await fileLocks.AcquireAsync(id, ct);
        await db.Entry(nook.Output).ReloadAsync(ct);
        NookSetup current = await SetupOfAsync(nook.Output, ct);
        if (current.Run is { ExitCode: null })
        {
            return new Result<NookSetup>(NooksErrors.SetupRunning);
        }

        Result<SetupStart> started = await StartSetupAsync(nook.Output, connection, resumeOnly: false, ct);
        if (started.Failed)
        {
            return new Result<NookSetup>(started.Error);
        }

        Result saved = await MarkPreparedAsync(nook.Output, copiedCheckpoint: null, started.Output, ct);
        if (saved.Failed)
        {
            return new Result<NookSetup>(saved.Error);
        }

        NookSetup again = await SetupOfAsync(nook.Output, ct);
        return new Result<NookSetup>(again);
    }

    private async Task<NookSetup> SetupOfAsync(Nook nook, CancellationToken ct)
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
