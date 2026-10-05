using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sandboxing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Nooks.Jobs;

// Puts nooks nobody uses to sleep, deletes the sandboxes of those asleep for long, and wakes them
// (src/ControlPlane/Modules/Nooks/README.md, "Sleep"). A nook stays awake for the sleep period after
// people reach it, as long as something keeps it awake, such as Chats while its agent works
// (NookActivity), and while its setup runs. Going to sleep and waking hold the nook's file lock, so neither meets the other or a file
// operation halfway.
internal sealed class NookSleeper(
    IDbContextFactory<NooksDbContext> databases,
    IServiceScopeFactory scopes,
    IEnumerable<ISandboxProvider> providers,
    DaemonConnections daemons,
    NookActivity activity,
    FileLocks fileLocks,
    NookReconciler reconciler,
    NooksSettings settings,
    TimeProvider time,
    ILogger<NookSleeper> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using PeriodicTimer timer = new PeriodicTimer(Interval, time);
        do
        {
            await PassAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    // Gives a sleeping nook compute again: a Paused or Stopped one resumes, and its resume scripts run
    // before anything else; an evicted one starts again from its latest checkpoint. Returns false when
    // its provider can't be asked, such as for a machine that is offline.
    public async Task<bool> WakeAsync(NookId nookId, CancellationToken ct)
    {
        activity.Used(nookId);
        using IDisposable held = await fileLocks.AcquireAsync(nookId, ct);
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == nookId, ct);
        if (nook is null || !nook.Asleep)
        {
            return true;
        }

        if (nook.Status == NookStatus.Evicted)
        {
            int? latest = await db.Checkpoints.Where(checkpoint => checkpoint.NookId == nookId).MaxAsync(checkpoint => (int?)checkpoint.Number, ct);
            await ChangeAsync(db, nook, evicted => { evicted.Replace(latest); return true; }, ct);
            reconciler.Wake();
            return true;
        }

        try
        {
            await ProviderOf(nook).ResumeAsync(SandboxKey.From(nookId.Value), ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.WakingFailed(logger, exception, nookId.Value);
            return false;
        }

        await ChangeAsync(db, nook, woken => { woken.Woke(); return true; }, ct);
        return true;
    }

    // One failure never stops the job: a failed nook or pass is logged and retried on the next pass.
    private async Task PassAsync(CancellationToken ct)
    {
        try
        {
            List<NookId> idle = await IdleAsync(ct);
            foreach (NookId nookId in idle)
            {
                await SleepAsync(nookId, ct);
            }

            List<NookId> longAsleep;
            await using (NooksDbContext db = await databases.CreateDbContextAsync(ct))
            {
                DateTimeOffset since = time.GetUtcNow() - settings.EvictAfter;
                longAsleep = await db.Nooks.Where(nook => nook.SleptAt < since).OrderBy(nook => nook.Id).Select(nook => nook.Id).Take(BatchSize).ToListAsync(ct);
            }

            foreach (NookId nookId in longAsleep)
            {
                await EvictAsync(nookId, ct);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.SleepPassFailed(logger, exception);
        }
    }

    // Running nooks with their daemon here that nobody used for the sleep period and whose setup
    // isn't running, and nooks a failed pass left going to sleep.
    private async Task<List<NookId>> IdleAsync(CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        List<Candidate> candidates = await db.Nooks
            .Where(nook => nook.Status == NookStatus.Running || nook.Status == NookStatus.Sleeping)
            .OrderBy(nook => nook.Id)
            .Select(nook => new Candidate(nook.Id, nook.Status, nook.SetupProcessId))
            .ToListAsync(ct);
        List<ProcessId> setups = [.. candidates.Where(candidate => candidate.Setup is not null).Select(candidate => candidate.Setup!.Value)];
        List<ProcessId> running = await db.Processes.Where(process => setups.Contains(process.Id) && process.ExitCode == null).Select(process => process.Id).ToListAsync(ct);
        HashSet<ProcessId> settingUp = [.. running];
        return [.. candidates
            .Where(candidate => candidate.Status == NookStatus.Sleeping
                || (daemons.IsConnected(candidate.Id) && activity.Idle(candidate.Id) && (candidate.Setup is not ProcessId setup || !settingUp.Contains(setup))))
            .Select(candidate => candidate.Id)
            .Take(BatchSize)];
    }

    // Keeps the files people changed since the latest checkpoint, then releases the nook's compute,
    // unless someone used it meanwhile. Its daemon's connection ends; processes end with it when its
    // provider keeps only files.
    private async Task SleepAsync(NookId nookId, CancellationToken ct)
    {
        try
        {
            await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
            {
                Result kept = await scope.ServiceProvider.GetRequiredService<NooksApi>().CheckpointBeforeSleepAsync(nookId, ct);
                if (kept.Failed)
                {
                    Log.NotAsleep(logger, nookId.Value, kept.Error.Message);
                    return;
                }
            }

            using IDisposable held = await fileLocks.AcquireAsync(nookId, ct);
            await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
            Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == nookId, ct);
            bool stillIdle = activity.Idle(nookId);
            if (nook is null || (nook.Status == NookStatus.Running && !stillIdle))
            {
                return;
            }

            bool falling = await ChangeAsync(db, nook, awake => awake.FallAsleep() || awake.Status == NookStatus.Sleeping, ct);
            if (!falling)
            {
                return;
            }

            Result<SandboxObservation> suspended = await ProviderOf(nook).SuspendAsync(SandboxKey.From(nookId.Value), ct);
            if (suspended.Failed)
            {
                throw new InvalidOperationException("Suspending the sandbox of nook " + nookId.Value + " failed: " + suspended.Error.Message);
            }

            NookStatus asleep = suspended.Output.State == SandboxState.Paused ? NookStatus.Paused : NookStatus.Stopped;
            if (asleep == NookStatus.Stopped)
            {
                await EndProcessesAsync(db, nookId, ct);
            }

            await ChangeAsync(db, nook, sleeping => { sleeping.FellAsleep(asleep, time); return true; }, ct);
            daemons.Drop(nookId);
            Log.NookAsleep(logger, nookId.Value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.SleepingFailed(logger, exception, nookId.Value);
        }
    }

    // Deletes the sandbox of a nook asleep for long; its files are in its latest checkpoint, and its
    // processes end.
    private async Task EvictAsync(NookId nookId, CancellationToken ct)
    {
        try
        {
            using IDisposable held = await fileLocks.AcquireAsync(nookId, ct);
            await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
            Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == nookId, ct);
            if (nook is not { Status: NookStatus.Paused or NookStatus.Stopped })
            {
                return;
            }

            await ProviderOf(nook).DeleteAsync(SandboxKey.From(nookId.Value), ct);
            await EndProcessesAsync(db, nookId, ct);
            await ChangeAsync(db, nook, asleep => { asleep.Evict(); return true; }, ct);
            Log.NookEvicted(logger, nookId.Value);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.SleepingFailed(logger, exception, nookId.Value);
        }
    }

    // Processes that ran in a nook whose processes are gone end as lost.
    private async Task EndProcessesAsync(NooksDbContext db, NookId nookId, CancellationToken ct)
    {
        DateTimeOffset now = time.GetUtcNow();
        await db.Processes.Where(process => process.NookId == nookId && process.ExitCode == null)
            .ExecuteUpdateAsync(set => set.SetProperty(process => process.ExitCode, ProcessExited.Lost).SetProperty(process => process.ExitedAt, now), ct);
    }

    // Applies a change and saves it; returns false when the change doesn't apply. The nook is held by
    // its file lock, so a conflict is its daemon's report of disk or connection, after which the nook is
    // read again and the change applied to what it is now.
    private static async Task<bool> ChangeAsync(NooksDbContext db, Nook nook, Func<Nook, bool> change, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            bool applies = change(nook);
            if (!applies)
            {
                return false;
            }

            Result saved = await db.SaveAsync(ct);
            if (!saved.Failed)
            {
                return true;
            }

            if (saved.Error != ModuleDbContextExtensions.ConcurrencyConflict)
            {
                throw new InvalidOperationException("Saving nook " + nook.Id.Value + " failed: " + saved.Error.Message);
            }

            await db.Entry(nook).ReloadAsync(ct);
        }

        throw new InvalidOperationException("Nook " + nook.Id.Value + " kept changing; the next pass tries again.");
    }

    // Providers are checked when a nook is recorded.
    private ISandboxProvider ProviderOf(Nook nook)
    {
        return providers.Single(candidate => string.Equals(candidate.Name, nook.Provider, StringComparison.Ordinal));
    }

    private sealed record Candidate(NookId Id, NookStatus Status, ProcessId? Setup);
}
