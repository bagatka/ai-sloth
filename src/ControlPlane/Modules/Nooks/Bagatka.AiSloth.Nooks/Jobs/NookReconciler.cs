using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sandboxing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Nooks.Jobs;

// Makes providers match the records: creates the sandboxes of new nooks and deletes those of deleted
// ones (src/ControlPlane/Modules/Nooks/README.md, "Background work"). Not handled yet: sandboxes
// without a record, and running nooks whose sandbox fails; both need a comparison with each
// provider's ListAsync.
internal sealed class NookReconciler(
    IDbContextFactory<NooksDbContext> databases,
    IEnumerable<ISandboxProvider> providers,
    NooksSettings settings,
    TimeProvider time,
    ILogger<NookReconciler> logger) : BackgroundService
{
    private const int BatchSize = 20;
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);

    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    // Runs a pass now instead of at the next interval, such as right after a nook is recorded.
    public void Wake()
    {
        _wake.Writer.TryWrite(true);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
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
                case NookStatus.Running or NookStatus.Paused or NookStatus.Stopped or NookStatus.Unreachable or NookStatus.Failed:
                    break;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.ReconcilingFailed(logger, exception, nookId.Value);
        }
    }

    // Creates the sandbox unless it exists. The daemon connecting then makes the nook Running.
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

        // Saved first, so the daemon of the sandbox about to exist can prove itself.
        string token = nook.IssueDaemonToken();
        if ((await db.SaveAsync(ct)).IsError(out _))
        {
            return;
        }

        Result<SandboxObservation> created = await provider.CreateAsync(Spec(nook, token), ct);
        if (!created.TryGetValue(out _, out Error? error))
        {
            await FailAsync(db, nook, error.Message, ct);
        }
    }

    private async Task DeleteSandboxAsync(NooksDbContext db, ISandboxProvider provider, Nook nook, CancellationToken ct)
    {
        await provider.DeleteAsync(SandboxKey.From(nook.Id.Value), ct);

        // A deleted nook's processes have no rules left to protect, so they go in bulk.
        await db.Processes.Where(process => process.NookId == nook.Id).ExecuteDeleteAsync(ct);
        db.Nooks.Remove(nook);
        if (!(await db.SaveAsync(ct)).IsError(out _))
        {
            Log.NookDeleted(logger, nook.Id.Value);
        }
    }

    private async Task FailAsync(NooksDbContext db, Nook nook, string reason, CancellationToken ct)
    {
        nook.Fail();
        if (!(await db.SaveAsync(ct)).IsError(out _))
        {
            Log.NookFailed(logger, nook.Id.Value, reason);
        }
    }

    private SandboxSpec Spec(Nook nook, string token)
    {
        return new SandboxSpec(
            SandboxKey.From(nook.Id.Value),
            new SandboxSource(new SandboxImage(settings.Image)),
            new SandboxResources(settings.CpuMillicores, settings.MemoryMebibytes),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["SLOTHD_CONTROL_PLANE_URL"] = settings.DaemonUrl.AbsoluteUri,
                ["SLOTHD_NOOK_ID"] = nook.Id.Value.ToString("D", CultureInfo.InvariantCulture),
                ["SLOTHD_TOKEN"] = token,
            });
    }
}
