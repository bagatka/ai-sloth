using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.ObjectStorage;
using Bagatka.Sandboxing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Nooks.Jobs;

// A nook's life at its provider (README, "Lifecycle"), one job that makes each sandbox
// match its record: it creates the sandboxes of new nooks, from a ready copy when one matches; brings
// back those whose sandbox was lost, from their latest checkpoint; puts nooks nobody uses to sleep,
// and deletes the sandboxes of those asleep for long; deletes those of deleted nooks; and hourly the
// ready copies nobody uses. It also wakes nooks for the operations that use them. Each nook has one
// piece of work at a time (NookWork), so a slow or hung provider holds up only the nooks it serves,
// and going to sleep and waking hold the nook's file lock, so neither meets a file operation halfway.
// Hourly it also deletes sandboxes no nook records, such as those a reset database left behind.
internal sealed class NookLifecycle(
    IDbContextFactory<NooksDbContext> databases,
    IEnumerable<ISandboxProvider> providers,
    DaemonConnections daemons,
    NookActivity activity,
    NookStarts starts,
    FileLocks fileLocks,
    NookProcesses processes,
    Checkpoints checkpoints,
    IObjectStorage storage,
    ReadyCopies readyCopies,
    NooksSettings settings,
    ActiveInstance active,
    TimeProvider time,
    ILogger<NookLifecycle> logger) : BackgroundService
{
    private const int Capacity = 32;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    // A trace for each nook worked on, named for the work: start, recover, sleep, evict, or delete.
    private static readonly ActivitySource Traces = new ActivitySource("Bagatka.AiSloth.Nooks");
    private const int OrphanBatch = 500;

    // Long enough for a machine on a slow connection to pull the nook image for its first sandbox, or
    // to keep a checkpoint of many changed files before sleeping; work that takes longer is hung.
    private static readonly TimeSpan Deadline = TimeSpan.FromHours(1);

    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    // When the next pass deletes the ready copies nobody uses: at the first after a start.
    private DateTimeOffset _nextPrune = DateTimeOffset.MinValue;

    // Runs a pass now instead of at the next interval, such as right after a nook is recorded.
    public void Wake()
    {
        _wake.Writer.TryWrite(true);
    }

    // The nook's daemon connection, waking the nook first when it sleeps and waiting for its daemon to
    // dial in: NotReady when the nook can't run processes, can't be woken, or its daemon doesn't come;
    // TooManyAwake when it sleeps and its workspace has no room. A person reaching it uses it, which
    // keeps it awake for the sleep period; the use counts before the status is read, so a nook about to
    // fall asleep either sees it and stays awake, or is seen asleep and woken.
    // Not handled: an operation in the instant between a nook's last idle check and its going to
    // sleep reaches a daemon about to stop, and may need repeating.
    public async Task<Result<DaemonConnection>> ConnectAsync(NooksDbContext db, Actor actor, NookId nookId, CancellationToken ct)
    {
        if (actor is UserActor)
        {
            activity.Used(nookId);
        }

        NookStatus status = await db.Nooks.Where(nook => nook.Id == nookId).Select(nook => nook.Status).SingleAsync(ct);
        if (status is NookStatus.Failed or NookStatus.Deleting)
        {
            return new Result<DaemonConnection>(NooksErrors.NotReady);
        }

        if (status == NookStatus.Asleep)
        {
            Result woke = await WakeAsync(nookId, actor, ct);
            if (woke.Failed)
            {
                return new Result<DaemonConnection>(woke.Error);
            }
        }

        DaemonConnection? connection = await daemons.WaitAsync(nookId, DaemonConnections.Patience, ct);
        return connection is null ? new Result<DaemonConnection>(NooksErrors.NotReady) : new Result<DaemonConnection>(connection);
    }

    // Gives a sleeping nook compute again, for the actor, once its workspace has room: one its
    // provider suspended resumes, and its resume scripts run before anything else; one whose sandbox
    // was deleted starts again from its latest checkpoint. NotReady when its provider can't be asked,
    // such as for a machine that is offline.
    public async Task<Result> WakeAsync(NookId nookId, Actor actor, CancellationToken ct)
    {
        activity.Used(nookId);
        using IDisposable held = await fileLocks.AcquireAsync(nookId, ct);
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == nookId, ct);
        if (nook is null || !nook.Asleep)
        {
            return new Result(new Success());
        }

        Result room = await MakeRoomAsync(db, nook.WorkspaceId, ct);
        if (room.Failed)
        {
            return room;
        }

        if (nook.Evicted)
        {
            int? latest = await LatestCheckpointAsync(db, nookId, ct);
            nook.Replace(latest);
            starts.Began(nookId, "restored");
        }
        else
        {
            starts.Began(nookId, "woken");
            try
            {
                await ProviderOf(nook).ResumeAsync(SandboxKey.From(nookId.Value), ct);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                starts.Forget(nookId);
                Log.WakingFailed(logger, exception, nookId.Value);
                return new Result(NooksErrors.NotReady);
            }

            nook.Woke();
        }

        Result saved = await db.SaveAsync(ct);
        Wake();
        string wokenFor = actor.ToLogValue();
        Log.NookWoke(logger, nookId.Value, wokenFor);
        return saved;
    }

    // At most MaxAwakePerWorkspace of a workspace's nooks are awake, those due to sleep aside: one more
    // starting or waking first sends the least recently used of the others that nothing keeps busy to
    // sleep, on the next pass; when every one of them is busy, TooManyAwake.
    public async Task<Result> MakeRoomAsync(NooksDbContext db, WorkspaceId workspaceId, CancellationToken ct)
    {
        List<Candidate> awake = await db.Nooks
            .Where(nook => nook.WorkspaceId == workspaceId && (nook.Status == NookStatus.Starting || nook.Status == NookStatus.Ready))
            .Select(nook => new Candidate(nook.Id, nook.Status, nook.SetupProcessId))
            .ToListAsync(ct);
        List<Candidate> staying = [.. awake.Where(nook => !activity.Idle(nook.Id))];
        if (staying.Count < settings.MaxAwakePerWorkspace)
        {
            return new Result(new Success());
        }

        NookId? idlest = activity.IdlestOf([.. staying.Where(nook => nook.Status == NookStatus.Ready).Select(nook => nook.Id)]);
        if (idlest is null)
        {
            return new Result(NooksErrors.TooManyAwake);
        }

        activity.SleepSoon(idlest.Value);
        Wake();
        return new Result(new Success());
    }

    // Only the active instance runs it (PATTERNS.md, entry 23).
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return active.RunAsync(WorkAsync, stoppingToken);
    }

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        NookWork work = new NookWork(Capacity, Deadline, time, logger);
        Task pruning = Task.CompletedTask;
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await PassAsync(work, stoppingToken);
                if (time.GetUtcNow() >= _nextPrune && pruning.IsCompleted)
                {
                    _nextPrune = time.GetUtcNow() + PruneInterval;
                    pruning = PruneAsync(stoppingToken);
                }

                using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                await Task.WhenAny(_wake.Reader.WaitToReadAsync(waiting.Token).AsTask(), Task.Delay(Interval, time, waiting.Token));
                await waiting.CancelAsync();
                _wake.Reader.TryRead(out _);
            }
        }
        finally
        {
            await work.StoppedAsync();
            await pruning;
        }
    }

    // Starts work on the nooks due, except those with work running. One failure never stops the job:
    // a failed nook or pass is logged and retried on the next pass.
    private async Task PassAsync(NookWork work, CancellationToken ct)
    {
        try
        {
            await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
            List<NookId> busy = work.Busy();
            DateTimeOffset longAgo = time.GetUtcNow() - settings.EvictAfter;
            List<Candidate> candidates = await db.Nooks
                .Where(nook => !busy.Contains(nook.Id) && (nook.Status != NookStatus.Asleep || (!nook.Evicted && nook.SleptAt < longAgo)) && nook.Status != NookStatus.Failed)
                .OrderBy(nook => nook.Id)
                .Select(nook => new Candidate(nook.Id, nook.Status, nook.SetupProcessId))
                .ToListAsync(ct);
            List<ProcessId> setups = [.. candidates.Where(candidate => candidate.Setup is not null).Select(candidate => candidate.Setup!.Value)];
            List<ProcessId> running = await db.Processes.Where(process => setups.Contains(process.Id) && process.ExitCode == null).Select(process => process.Id).ToListAsync(ct);
            HashSet<ProcessId> settingUp = [.. running];
            foreach (Candidate candidate in candidates.Where(candidate => Due(candidate, settingUp)).Take(Capacity))
            {
                work.TryStart(candidate.Id, nookCt => WorkOnAsync(candidate.Id, nookCt), ct);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.PassFailed(logger, exception);
        }
    }

    // Whether the nook has work: it is being deleted or started, its daemon is away, nobody used it for
    // the sleep period while its setup isn't running, or it slept for long.
    // Not handled: a person's own long process keeps no nook awake; it sleeps with the nook, and ends
    // with it where the provider keeps only files.
    private bool Due(Candidate candidate, HashSet<ProcessId> settingUp)
    {
        bool connected = daemons.IsConnected(candidate.Id);
        return candidate.Status switch
        {
            NookStatus.Deleting or NookStatus.Starting or NookStatus.Asleep => true,
            NookStatus.Ready or NookStatus.Offline when !connected => true,
            NookStatus.Ready => activity.Idle(candidate.Id) && (candidate.Setup is not ProcessId setup || !settingUp.Contains(setup)),
            NookStatus.Offline or NookStatus.Failed => false,
        };
    }

    // The nook's work, as its record and its daemon call for it now.
    private async Task WorkOnAsync(NookId nookId, CancellationToken ct)
    {
        using Activity? traced = Traces.StartActivity("nook work");
        traced?.SetTag("nook.id", nookId.Value.ToString("D", CultureInfo.InvariantCulture));
        try
        {
            await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
            Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == nookId, ct);
            if (nook is null)
            {
                return;
            }

            ISandboxProvider provider = ProviderOf(nook);
            bool connected = daemons.IsConnected(nook.Id);
            switch (nook.Status)
            {
                case NookStatus.Deleting:
                    traced?.DisplayName = "delete nook";
                    await DeleteSandboxAsync(db, provider, nook, ct);
                    break;
                case NookStatus.Starting when !connected:
                    traced?.DisplayName = "start nook";
                    await EnsureSandboxAsync(db, provider, nook, ct);
                    break;
                case NookStatus.Ready or NookStatus.Offline when !connected:
                    traced?.DisplayName = "recover nook";
                    await RecoverAsync(db, provider, nook, ct);
                    break;
                case NookStatus.Ready:
                    traced?.DisplayName = "sleep nook";
                    await SleepAsync(nook.Id, ct);
                    break;
                case NookStatus.Asleep:
                    traced?.DisplayName = "evict nook";
                    await EvictAsync(nook.Id, ct);
                    break;
                case NookStatus.Starting or NookStatus.Offline or NookStatus.Failed:
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            traced?.SetStatus(ActivityStatusCode.Error);
            Log.NookWorkFailed(logger, exception, nookId.Value);
        }
    }

    // Creates the sandbox unless it exists, and resumes one its provider suspended. A sandbox that ran
    // the nook's files and failed since, such as one that couldn't start again as the nook woke, is
    // replaced from the nook's latest checkpoint, like the one it took as it fell asleep; a new one
    // that failed fails the nook, as its image can't run. The daemon connecting then makes the nook Ready.
    private async Task EnsureSandboxAsync(NooksDbContext db, ISandboxProvider provider, Nook nook, CancellationToken ct)
    {
        SandboxKey key = SandboxKey.From(nook.Id.Value);
        SandboxObservation? sandbox = await provider.ObserveAsync(key, ct);
        switch (sandbox?.State)
        {
            case SandboxState.Failed when nook.SourcesReady:
                await ReplaceSandboxAsync(db, provider, nook, ct);
                return;
            case SandboxState.Failed:
                await FailAsync(db, nook, sandbox.Reason ?? "The provider reports the sandbox failed.", ct);
                return;
            case SandboxState.Paused or SandboxState.Stopped:
                starts.Began(nook.Id, "resumed");
                await provider.ResumeAsync(key, ct);
                return;
            case not null:
                return;
        }

        await CreateSandboxAsync(db, provider, nook, ct);
    }

    // Creates the nook's sandbox, from the ready copy matching its files, with its setup's work done,
    // or from its image.
    private async Task CreateSandboxAsync(NooksDbContext db, ISandboxProvider provider, Nook nook, CancellationToken ct)
    {
        // An image the deployment stopped offering has nothing to start from.
        string? image = settings.ImageOf(nook.Image);
        if (image is null)
        {
            await FailAsync(db, nook, "The deployment no longer offers its image.", ct);
            return;
        }

        ReadyCopy? copy = await readyCopies.FindAsync(db, nook, image, ct);
        nook.StartsFrom(copy?.MadeAt);

        // Saved first, so the daemon of the sandbox about to exist can prove itself.
        string token = nook.IssueDaemonToken();
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return;
        }

        starts.Began(nook.Id, copy is null ? "new" : "ready_copy", nook.CreatedAt);
        SandboxSource fromImage = new SandboxSource(new SandboxImage(image));
        Result<SandboxObservation> created = await provider.CreateAsync(Spec(nook, copy is null ? fromImage : new SandboxSource(SnapshotKey.From(copy.Snapshot)), token), ct);

        // A copy whose snapshot is gone at the provider goes, and the nook starts from its image.
        if (created.Failed && created.Error.Kind == ErrorKind.NotFound && copy is not null)
        {
            Log.ReadyCopyGone(logger, nook.Id.Value);
            db.ReadyCopies.Remove(copy);
            nook.StartsFrom(null);
            Result forgotten = await db.SaveAsync(ct);
            if (forgotten.Failed)
            {
                return;
            }

            created = await provider.CreateAsync(Spec(nook, fromImage, token), ct);
        }

        if (created.Failed)
        {
            await FailAsync(db, nook, created.Error.Message, ct);
        }
    }

    // A nook whose daemon is away: a sandbox that is still there only waits for its daemon, and one its
    // provider suspended, such as by its machine restarting, resumes, and its resume scripts run again;
    // its processes ended unless it kept its memory. A sandbox that is gone or failed is replaced. A
    // provider that can't be asked, such as a machine that is offline, makes the nook Offline, said
    // once; its daemon coming back makes it Ready.
    // Not handled: a daemon that stays away while its sandbox runs; the nook stays Ready, and calls
    // wait for it up to DaemonConnections.Patience, then answer NotReady.
    private async Task RecoverAsync(NooksDbContext db, ISandboxProvider provider, Nook nook, CancellationToken ct)
    {
        SandboxKey key = SandboxKey.From(nook.Id.Value);
        SandboxObservation? sandbox;
        try
        {
            sandbox = await provider.ObserveAsync(key, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            bool news = nook.Offline();
            if (news)
            {
                Result marked = await db.SaveAsync(ct);
                if (!marked.Failed)
                {
                    Log.NookOffline(logger, exception, nook.Id.Value);
                }
            }

            return;
        }

        if (sandbox is { State: SandboxState.Stopped or SandboxState.Paused })
        {
            if (sandbox.State == SandboxState.Stopped)
            {
                await processes.EndAllAsync(db, nook.Id, ct);
            }

            starts.Began(nook.Id, "recovered");
            await provider.ResumeAsync(key, ct);
            nook.Woke();
            await db.SaveAsync(ct);
            return;
        }

        if (sandbox is null || sandbox.State == SandboxState.Failed)
        {
            await ReplaceSandboxAsync(db, provider, nook, ct);
        }
    }

    // The nook's sandbox is gone or failed, such as a container removed or a machine lost with its
    // disk: a new one starts, whose files come from the latest checkpoint, and the processes that ran
    // in the old one end with exit code -1.
    private async Task ReplaceSandboxAsync(NooksDbContext db, ISandboxProvider provider, Nook nook, CancellationToken ct)
    {
        starts.Began(nook.Id, "replaced");
        await provider.DeleteAsync(SandboxKey.From(nook.Id.Value), ct);
        int? latest = await LatestCheckpointAsync(db, nook.Id, ct);

        // Without a checkpoint, repositories are copied in again; a copy of another nook is copied again.
        if (latest is null && nook.CopyOf is null)
        {
            List<SourceCopy> copies = await db.SourceCopies.Where(copy => copy.NookId == nook.Id).ToListAsync(ct);
            foreach (SourceCopy copy in copies)
            {
                copy.Lost();
            }
        }

        await processes.EndAllAsync(db, nook.Id, ct);
        nook.Replace(latest);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return;
        }

        Log.NookReplaced(logger, nook.Id.Value);
        await EnsureSandboxAsync(db, provider, nook, ct);
    }

    // Keeps the files people changed since the latest checkpoint, then releases the nook's compute,
    // unless someone used it meanwhile. Its daemon's connection ends; processes end with it when its
    // provider keeps only files.
    private async Task SleepAsync(NookId nookId, CancellationToken ct)
    {
        Result kept = await KeepFilesAsync(nookId, ct);
        if (kept.Failed)
        {
            Log.NotAsleep(logger, nookId.Value, kept.Error.Message);
            return;
        }

        using IDisposable held = await fileLocks.AcquireAsync(nookId, ct);
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == nookId, ct);
        bool stillIdle = activity.Idle(nookId);
        if (nook?.Status != NookStatus.Ready || !stillIdle)
        {
            return;
        }

        Result<SandboxObservation> suspended = await ProviderOf(nook).SuspendAsync(SandboxKey.From(nookId.Value), ct);
        if (suspended.Failed)
        {
            throw new InvalidOperationException("Suspending the sandbox of nook " + nookId.Value + " failed: " + suspended.Error.Message);
        }

        if (suspended.Output.State != SandboxState.Paused)
        {
            await processes.EndAllAsync(db, nookId, ct);
        }

        nook.FellAsleep(time);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            throw new InvalidOperationException("Saving that nook " + nookId.Value + " fell asleep failed: " + saved.Error.Message);
        }

        daemons.Drop(nookId);
        Log.NookAsleep(logger, nookId.Value);
    }

    // Before a nook falls asleep, its files are kept as a checkpoint if they changed since the latest,
    // so a sleep long enough to delete its sandbox loses nothing; agents' changes are kept after each
    // turn already. A nook whose files never arrived has nothing to keep.
    private async Task<Result> KeepFilesAsync(NookId nookId, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == nookId, ct);
        if (nook is not { SourcesReady: true })
        {
            return new Result(new Success());
        }

        DaemonConnection? connection = await daemons.WaitAsync(nookId, DaemonConnections.Patience, ct);
        if (connection is null)
        {
            return new Result(NooksErrors.NotReady);
        }

        Result<Checkpoint> saved = await checkpoints.SaveAsync(db, nook, connection, "Before sleeping", onlyIfChanged: true, ct);
        return saved.Failed ? new Result(saved.Error) : new Result(new Success());
    }

    // Deletes the sandbox of a nook asleep for long; its files are in its latest checkpoint, and its
    // processes end.
    private async Task EvictAsync(NookId nookId, CancellationToken ct)
    {
        using IDisposable held = await fileLocks.AcquireAsync(nookId, ct);
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == nookId, ct);
        if (nook is not { Status: NookStatus.Asleep, Evicted: false })
        {
            return;
        }

        await ProviderOf(nook).DeleteAsync(SandboxKey.From(nookId.Value), ct);
        await processes.EndAllAsync(db, nookId, ct);
        nook.Evict();
        Result saved = await db.SaveAsync(ct);
        if (!saved.Failed)
        {
            Log.NookEvicted(logger, nookId.Value);
        }
    }

    private async Task DeleteSandboxAsync(NooksDbContext db, ISandboxProvider provider, Nook nook, CancellationToken ct)
    {
        await provider.DeleteAsync(SandboxKey.From(nook.Id.Value), ct);

        // A deleted nook's processes and checkpoints have no rules left to protect, so they go in
        // bulk, the checkpoints' parts with them, and then their bundles.
        await db.Checkpoints.Where(checkpoint => checkpoint.NookId == nook.Id).ExecuteDeleteAsync(ct);
        await db.Processes.Where(process => process.NookId == nook.Id).ExecuteDeleteAsync(ct);
        await storage.DeleteAsync(Checkpoint.Prefix(nook.Id), ct);
        db.Nooks.Remove(nook);
        Result saved = await db.SaveAsync(ct);
        if (!saved.Failed)
        {
            activity.Forget(nook.Id);
            starts.Forget(nook.Id);
            Log.NookDeleted(logger, nook.Id.Value);
        }
    }

    // A provider that doesn't answer stops only pruning, until the deadline.
    private async Task PruneAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        using CancellationTokenSource late = new CancellationTokenSource(Deadline, time);
        using CancellationTokenSource ending = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, late.Token);
        try
        {
            await readyCopies.PruneAsync(ending.Token);
            await PruneOrphansAsync(ending.Token);
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            Log.PruningTooLong(logger, Deadline);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.PruneFailed(logger, exception);
        }
    }

    // Sandboxes no nook records: a sandbox is created only after its nook is recorded, and the records
    // are read after the list, so one listed without a record lost it, as when the database was reset.
    // Each provider lists only this deployment's scope; one that can't be asked waits for the next hour.
    private async Task PruneOrphansAsync(CancellationToken ct)
    {
        foreach (ISandboxProvider provider in providers)
        {
            try
            {
                List<SandboxKey> listed = [];
                await foreach (SandboxObservation sandbox in provider.ListAsync(ct))
                {
                    listed.Add(sandbox.Key);
                }

                foreach (SandboxKey[] batch in listed.Chunk(OrphanBatch))
                {
                    await DeleteOrphansAsync(provider, batch, ct);
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.PruningFailed(logger, exception, provider.Name);
            }
        }
    }

    private async Task DeleteOrphansAsync(ISandboxProvider provider, SandboxKey[] batch, CancellationToken ct)
    {
        List<NookId> listed = [.. batch.Select(key => NookId.From(key.Value))];
        List<NookId> recorded;
        await using (NooksDbContext db = await databases.CreateDbContextAsync(ct))
        {
            recorded = await db.Nooks.Where(nook => listed.Contains(nook.Id)).Select(nook => nook.Id).ToListAsync(ct);
        }

        foreach (NookId orphan in listed.Except(recorded))
        {
            await provider.DeleteAsync(SandboxKey.From(orphan.Value), ct);
            Log.OrphanDeleted(logger, orphan.Value, provider.Name);
        }
    }

    private async Task FailAsync(NooksDbContext db, Nook nook, string reason, CancellationToken ct)
    {
        nook.Fail();
        Result saved = await db.SaveAsync(ct);
        if (!saved.Failed)
        {
            starts.Failed(nook);
            Log.NookFailed(logger, nook.Id.Value, reason);
        }
    }

    private static async Task<int?> LatestCheckpointAsync(NooksDbContext db, NookId nookId, CancellationToken ct)
    {
        return await db.Checkpoints.Where(checkpoint => checkpoint.NookId == nookId).MaxAsync(checkpoint => (int?)checkpoint.Number, ct);
    }

    // Providers are checked when a nook is recorded.
    private ISandboxProvider ProviderOf(Nook nook)
    {
        return providers.Single(candidate => string.Equals(candidate.Name, nook.Provider, StringComparison.Ordinal));
    }

    private SandboxSpec Spec(Nook nook, SandboxSource source, string token)
    {
        Dictionary<string, string> environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SLOTHD_CONTROL_PLANE_URL"] = settings.DaemonUrl.AbsoluteUri,
            ["SLOTHD_NOOK_ID"] = nook.Id.Value.ToString("D", CultureInfo.InvariantCulture),
            ["SLOTHD_TOKEN"] = token,
        };
        if (settings.CrashReportsHost is not null && settings.CrashReportsToken is not null)
        {
            environment["SLOTHD_POSTHOG_HOST"] = settings.CrashReportsHost.AbsoluteUri;
            environment["SLOTHD_POSTHOG_TOKEN"] = settings.CrashReportsToken;
        }

        return new SandboxSpec(
            SandboxKey.From(nook.Id.Value),
            source,
            new SandboxResources(settings.CpuMillicores, settings.MemoryMebibytes),
            environment,
            nook.Location);
    }

    private sealed record Candidate(NookId Id, NookStatus Status, ProcessId? Setup);
}
