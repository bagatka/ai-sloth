using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sandboxing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Nooks;

// Ready copies (README, "Ready copies"): taking one after a setup that took a while, finding the one a
// new sandbox starts from, and deleting those nobody uses. This module is the only user of its
// providers' snapshots, and every snapshot it takes is a ready copy.
internal sealed class ReadyCopies(
    IDbContextFactory<NooksDbContext> databases,
    IEnumerable<ISandboxProvider> providers,
    NooksSettings settings,
    TimeProvider time,
    ILogger<ReadyCopies> logger)
{
    private const int BatchSize = 20;

    // How old a snapshot no ready copy holds is before it goes: one just taken is saved right after.
    private static readonly TimeSpan Settled = TimeSpan.FromHours(1);

    // Snapshots the nook's sandbox as the ready copy for nooks with its files, replacing the one they
    // had. Only nooks with repositories share their files. A copy that can't be taken is logged: it
    // only makes later nooks faster.
    public async Task TakeAsync(Nook nook, CancellationToken ct)
    {
        try
        {
            await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
            List<SourceCopy> repositories = await db.SourceCopies.AsNoTracking().Where(copy => copy.NookId == nook.Id).ToListAsync(ct);
            string? image = settings.ImageOf(nook.Harness);
            if (repositories.Count == 0 || image is null)
            {
                return;
            }

            string match = ReadyCopy.MatchOf(nook, image, repositories);
            ISandboxProvider provider = ProviderOf(nook.Provider);
            SnapshotKey snapshot = SnapshotKey.From(Guid.CreateVersion7());
            Result<SnapshotObservation> taken = await provider.SnapshotAsync(SandboxKey.From(nook.Id.Value), snapshot, ct);
            if (taken.Failed)
            {
                throw new InvalidOperationException("The provider didn't snapshot the nook: " + taken.Error.Message);
            }

            ReadyCopy? existing = await db.ReadyCopies.SingleOrDefaultAsync(copy => copy.Match == match, ct);
            Guid? replaced = null;
            if (existing is null)
            {
                db.ReadyCopies.Add(ReadyCopy.Make(match, nook, snapshot.Value, time));
            }
            else
            {
                replaced = existing.Replace(snapshot.Value, nook.Id, time);
            }

            // Another nook with these files saved its copy meanwhile, which is as good: this one goes.
            Result saved = await db.SaveAsync(ct);
            if (saved.Failed)
            {
                await provider.DeleteSnapshotAsync(snapshot, ct);
                return;
            }

            if (replaced is Guid old)
            {
                await provider.DeleteSnapshotAsync(SnapshotKey.From(old), ct);
            }

            Log.ReadyCopyTaken(logger, nook.Id.Value, match);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Log.ReadyCopyFailed(logger, exception, nook.Id.Value);
        }
    }

    // The ready copy a new sandbox for the nook starts from, marked used for the caller to save: the one
    // matching its files, its own repositories or those of the nook it copies, unless it starts from
    // scratch.
    public async Task<ReadyCopy?> FindAsync(NooksDbContext db, Nook nook, string image, CancellationToken ct)
    {
        if (nook.FromScratch)
        {
            return null;
        }

        List<SourceCopy> repositories = await db.SourceCopies.AsNoTracking().Where(copy => copy.NookId == nook.Id).ToListAsync(ct);
        if (repositories.Count == 0 && nook.CopyOf is NookId source)
        {
            repositories = await db.SourceCopies.AsNoTracking().Where(copy => copy.NookId == source).ToListAsync(ct);
        }

        if (repositories.Count == 0)
        {
            return null;
        }

        string match = ReadyCopy.MatchOf(nook, image, repositories);
        ReadyCopy? copy = await db.ReadyCopies.SingleOrDefaultAsync(found => found.Match == match, ct);
        if (copy is null)
        {
            Log.ReadyCopyMissing(logger, nook.Id.Value, match);
            return null;
        }

        Log.ReadyCopyFound(logger, nook.Id.Value, match);
        copy.Used(time);
        return copy;
    }

    // Deletes ready copies nobody started from for a week, and snapshots no ready copy holds, such as
    // one a failed save or a database reset left, once they are an hour old. A provider that can't be
    // asked, such as a machine that is offline, is asked again next time.
    public async Task PruneAsync(CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        DateTimeOffset unusedSince = time.GetUtcNow() - ReadyCopy.KeptUnused;
        List<ReadyCopy> unused = await db.ReadyCopies.Where(copy => copy.UsedAt < unusedSince).OrderBy(copy => copy.UsedAt).Take(BatchSize).ToListAsync(ct);
        db.ReadyCopies.RemoveRange(unused);
        await db.SaveAsync(ct);

        List<Guid> heldList = await db.ReadyCopies.Select(copy => copy.Snapshot).ToListAsync(ct);
        HashSet<Guid> held = [.. heldList];
        DateTimeOffset settledBefore = time.GetUtcNow() - Settled;
        foreach (ISandboxProvider provider in providers)
        {
            try
            {
                await foreach (SnapshotObservation snapshot in provider.ListSnapshotsAsync(ct))
                {
                    if (!held.Contains(snapshot.Key.Value) && snapshot.CreatedAt < settledBefore)
                    {
                        await provider.DeleteSnapshotAsync(snapshot.Key, ct);
                    }
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                Log.PruningFailed(logger, exception, provider.Name);
            }
        }
    }

    // Providers are checked when a nook is recorded.
    private ISandboxProvider ProviderOf(string name)
    {
        return providers.Single(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
    }
}
