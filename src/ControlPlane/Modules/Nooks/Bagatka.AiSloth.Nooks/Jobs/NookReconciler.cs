using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.ObjectStorage;
using Bagatka.Sandboxing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Nooks.Jobs;

// Makes providers match the records: creates the sandboxes of new nooks, from a ready copy when one
// matches, replaces those running nooks lost, deletes those of deleted ones, and hourly the ready copies
// nobody uses (src/ControlPlane/Modules/Nooks/README.md, "Background work"). Not handled yet: sandboxes
// without a record, which needs a comparison with each provider's ListAsync.
internal sealed class NookReconciler(
    IDbContextFactory<NooksDbContext> databases,
    IEnumerable<ISandboxProvider> providers,
    DaemonConnections daemons,
    IObjectStorage storage,
    ReadyCopies readyCopies,
    NooksSettings settings,
    ActiveInstance active,
    TimeProvider time,
    ILogger<NookReconciler> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PruneInterval = TimeSpan.FromHours(1);

    // When the next pass deletes the ready copies nobody uses: at the first after a start.
    private DateTimeOffset _nextPrune = DateTimeOffset.MinValue;

    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    // Runs a pass now instead of at the next interval, such as right after a nook is recorded.
    public void Wake()
    {
        _wake.Writer.TryWrite(true);
    }

    // Only the active instance runs it (PATTERNS.md, entry 23).
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return active.RunAsync(WorkAsync, stoppingToken);
    }

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await PassAsync(stoppingToken);
            using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            await Task.WhenAny(_wake.Reader.WaitToReadAsync(waiting.Token).AsTask(), Task.Delay(Interval, time, waiting.Token));
            await waiting.CancelAsync();
            _wake.Reader.TryRead(out _);
        }
    }

    // One failure never stops the job: a failed nook or pass is logged and retried on the next pass.
    private async Task PassAsync(CancellationToken ct)
    {
        try
        {
            foreach (NookStatus status in new[] { NookStatus.Deleting, NookStatus.Creating })
            {
                List<NookId> due;
                await using (NooksDbContext db = await databases.CreateDbContextAsync(ct))
                {
                    due = await db.Nooks.Where(nook => nook.Status == status).OrderBy(nook => nook.Id).Select(nook => nook.Id).Take(BatchSize).ToListAsync(ct);
                }

                foreach (NookId nookId in due)
                {
                    await ReconcileAsync(nookId, ct);
                }
            }

            // Running nooks whose daemon is away, such as for a moment after a deploy, on a machine that
            // is offline, or for good because their sandbox is gone.
            List<NookId> running;
            await using (NooksDbContext db = await databases.CreateDbContextAsync(ct))
            {
                running = await db.Nooks.Where(nook => nook.Status == NookStatus.Running || nook.Status == NookStatus.Unreachable)
                    .OrderBy(nook => nook.Id).Select(nook => nook.Id).ToListAsync(ct);
            }

            foreach (NookId nookId in running.Where(nookId => !daemons.IsConnected(nookId)).Take(BatchSize))
            {
                await ReconcileAsync(nookId, ct);
            }

            if (time.GetUtcNow() >= _nextPrune)
            {
                _nextPrune = time.GetUtcNow() + PruneInterval;
                await readyCopies.PruneAsync(ct);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.PassFailed(logger, exception);
        }
    }

    private async Task ReconcileAsync(NookId nookId, CancellationToken ct)
    {
        try
        {
            await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
            Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == nookId, ct);
            if (nook is null)
            {
                return;
            }

            // Providers are checked when a nook is recorded.
            ISandboxProvider provider = providers.Single(candidate => string.Equals(candidate.Name, nook.Provider, StringComparison.Ordinal));
            switch (nook.Status)
            {
                case NookStatus.Creating:
                    await EnsureSandboxAsync(db, provider, nook, ct);
                    break;
                case NookStatus.Deleting:
                    await DeleteSandboxAsync(db, provider, nook, ct);
                    break;
                case NookStatus.Running or NookStatus.Unreachable when !daemons.IsConnected(nook.Id):
                    await RecoverAsync(db, provider, nook, ct);
                    break;
                case NookStatus.Running or NookStatus.Paused or NookStatus.Stopped or NookStatus.Unreachable or NookStatus.Failed or NookStatus.Sleeping or NookStatus.Evicted:
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.ReconcilingFailed(logger, exception, nookId.Value);
        }
    }

    // Creates the sandbox unless it exists, from the ready copy matching the nook's files, with its
    // setup's work done, or from its image. The daemon connecting then makes the nook Running.
    private async Task EnsureSandboxAsync(NooksDbContext db, ISandboxProvider provider, Nook nook, CancellationToken ct)
    {
        SandboxObservation? sandbox = await provider.ObserveAsync(SandboxKey.From(nook.Id.Value), ct);
        if (sandbox is not null)
        {
            if (sandbox.State == SandboxState.Failed)
            {
                await FailAsync(db, nook, sandbox.Reason ?? "The provider reports the sandbox failed.", ct);
            }

            return;
        }

        // A harness the deployment stopped offering has no image to start from.
        string? image = settings.ImageOf(nook.Harness);
        if (image is null)
        {
            await FailAsync(db, nook, "The deployment no longer offers its harness.", ct);
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

    // A running nook whose sandbox is gone, or failed, such as a container removed or a machine lost
    // with its disk: a new sandbox starts, whose files come from the latest checkpoint, and the
    // processes that ran in the old one end with exit code -1. A sandbox that is still there only
    // waits for its daemon. A provider that can't be asked, such as a machine that is offline, makes
    // the nook Unreachable, said once; its daemon coming back makes it Running.
    // A sandbox suspended while its nook was awake, such as by its machine restarting, resumes, and its
    // resume scripts run again; its processes ended unless it kept its memory.
    private async Task ResumeAsync(NooksDbContext db, ISandboxProvider provider, Nook nook, SandboxState suspended, CancellationToken ct)
    {
        if (suspended == SandboxState.Stopped)
        {
            DateTimeOffset stoppedAt = time.GetUtcNow();
            await db.Processes.Where(process => process.NookId == nook.Id && process.ExitCode == null)
                .ExecuteUpdateAsync(set => set.SetProperty(process => process.ExitCode, ProcessExited.Lost).SetProperty(process => process.ExitedAt, stoppedAt), ct);
        }

        await provider.ResumeAsync(SandboxKey.From(nook.Id.Value), ct);
        nook.Woke();
        await db.SaveAsync(ct);
    }

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
            bool news = nook.Unreachable();
            if (news)
            {
                Result marked = await db.SaveAsync(ct);
                if (!marked.Failed)
                {
                    Log.NookUnreachable(logger, exception, nook.Id.Value);
                }
            }

            return;
        }

        if (sandbox is { State: SandboxState.Stopped or SandboxState.Paused })
        {
            await ResumeAsync(db, provider, nook, sandbox.State, ct);
            return;
        }

        if (sandbox is not null && sandbox.State != SandboxState.Failed)
        {
            return;
        }

        await provider.DeleteAsync(key, ct);
        int? latest = await db.Checkpoints.Where(checkpoint => checkpoint.NookId == nook.Id).MaxAsync(checkpoint => (int?)checkpoint.Number, ct);

        // Without a checkpoint, repositories are copied in again; a copy of another nook is copied again.
        if (latest is null && nook.CopyOf is null)
        {
            List<SourceCopy> copies = await db.SourceCopies.Where(copy => copy.NookId == nook.Id).ToListAsync(ct);
            foreach (SourceCopy copy in copies)
            {
                copy.Lost();
            }
        }

        DateTimeOffset lostAt = time.GetUtcNow();
        await db.Processes.Where(process => process.NookId == nook.Id && process.ExitCode == null)
            .ExecuteUpdateAsync(set => set.SetProperty(process => process.ExitCode, -1).SetProperty(process => process.ExitedAt, lostAt), ct);
        nook.Replace(latest);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return;
        }

        Log.NookReplaced(logger, nook.Id.Value);
        await EnsureSandboxAsync(db, provider, nook, ct);
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
            Log.NookDeleted(logger, nook.Id.Value);
        }
    }

    private async Task FailAsync(NooksDbContext db, Nook nook, string reason, CancellationToken ct)
    {
        nook.Fail();
        Result saved = await db.SaveAsync(ct);
        if (!saved.Failed)
        {
            Log.NookFailed(logger, nook.Id.Value, reason);
        }
    }

    private SandboxSpec Spec(Nook nook, SandboxSource source, string token)
    {
        return new SandboxSpec(
            SandboxKey.From(nook.Id.Value),
            source,
            new SandboxResources(settings.CpuMillicores, settings.MemoryMebibytes),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["SLOTHD_CONTROL_PLANE_URL"] = settings.DaemonUrl.AbsoluteUri,
                ["SLOTHD_NOOK_ID"] = nook.Id.Value.ToString("D", CultureInfo.InvariantCulture),
                ["SLOTHD_TOKEN"] = token,
            },
            nook.Location);
    }
}
