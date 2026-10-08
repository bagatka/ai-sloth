using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.ObjectStorage;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

// Folders AiSloth keeps outside every nook, which nooks sync a folder of theirs with
// (sync-folder.sh owns the merge): the history is git's, kept as bundles in object storage. Two
// nooks saving at once conflict on the folder's row, and the later one syncs again, merging the
// other's changes.
internal sealed class KeptFolders(IDbContextFactory<NooksDbContext> databases, NookProcesses processes, IObjectStorage storage, TimeProvider time)
{
    private const int MaxAttempts = 3;

    // A sync's answer: the folder's commit and the history the kept folder lacks. Folders hold up to
    // 16 MiB (sync-folder.sh), and history compresses.
    private const long MaxSyncOutput = 64L * 1024 * 1024;

    public async Task<Result> SyncAsync(DaemonConnection connection, string path, string name, CancellationToken ct)
    {
        Result<bool> synced = new Result<bool>(false);
        for (int attempt = 0; attempt < MaxAttempts && !synced.Failed && !synced.Output; attempt++)
        {
            synced = await SyncOnceAsync(connection, path, name, ct);
        }

        if (synced.Failed)
        {
            return new Result(synced.Error);
        }

        return synced.Output ? new Result(new Success()) : new Result(Failed("other nooks kept saving it."));
    }

    public async Task<IReadOnlyList<KeptFolder>> ListAsync(string prefix, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        return await db.KeptFolders.AsNoTracking().Where(folder => EF.Functions.Like(folder.Name, prefix + "%")).OrderBy(folder => folder.Name).ToListAsync(ct);
    }

    // The row goes first, then the history, so a row never points at nothing.
    public async Task DeleteAsync(string prefix, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        List<string> names = await db.KeptFolders.Where(folder => EF.Functions.Like(folder.Name, prefix + "%")).Select(folder => folder.Name).ToListAsync(ct);
        await db.KeptFolders.Where(folder => names.Contains(folder.Name)).ExecuteDeleteAsync(ct);
        foreach (string name in names)
        {
            await storage.DeleteAsync(KeptFolder.Prefix(name), ct);
        }
    }

    // Returns whether the sync is done: false when another nook saved the folder meanwhile.
    private async Task<Result<bool>> SyncOnceAsync(DaemonConnection connection, string path, string name, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        KeptFolder? folder = await db.KeptFolders.SingleOrDefaultAsync(found => found.Name == name, ct);
        List<string> bundles = [.. (folder?.Saves ?? []).Select(save => KeptFolder.BundleKey(name, save))];
        string history = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..16];
        string[] arguments = [path, history, folder?.Head ?? string.Empty, folder is { Folds: true } ? "full" : string.Empty];
        using MemoryStream output = new MemoryStream();
        string? missing = null;
        ProcessRun run = await processes.RunAsync(
            connection,
            Scripts.SyncFolder,
            arguments,
            NookProcesses.NoVariables,
            async (stream, token) => { missing = await WriteBundlesAsync(bundles, stream, token); },
            ScriptOutput.Small(output, MaxSyncOutput),
            ct);
        if (missing is not null)
        {
            return new Result<bool>(Failed("its history's bundle " + missing + " is missing from object storage."));
        }

        if (!run.Succeeded)
        {
            return new Result<bool>(Failed(run.Errors));
        }

        output.Position = 0;
        (string head, byte[]? bundle) = await ReadAsync(output, ct);
        if (head.Length == 0 || bundle is null || string.Equals(head, folder?.Head, StringComparison.Ordinal))
        {
            return new Result<bool>(true);
        }

        return await SaveAsync(db, folder, name, head, bundle, connection.NookId, ct);
    }

    // Saves what the nook's sync brought; false when another nook saved the folder meanwhile. The
    // bundle goes first, then the row that refers to it.
    private async Task<Result<bool>> SaveAsync(NooksDbContext db, KeptFolder? folder, string name, string head, byte[] bundle, NookId savedBy, CancellationToken ct)
    {
        using (MemoryStream content = new MemoryStream(bundle))
        {
            await storage.PutAsync(KeptFolder.BundleKey(name, head), content, ct);
        }

        List<string> replaced = [];
        if (folder is null)
        {
            db.KeptFolders.Add(KeptFolder.Keep(name, head, bundle.Length, savedBy, time));
        }
        else
        {
            replaced = folder.Saved(head, bundle.Length, savedBy, time);
        }

        Result saved = await db.SaveAsync(ct);
        bool raced = saved.Failed && (saved.Error == ModuleDbContextExtensions.ConcurrencyConflict || saved.Error == ModuleDbContextExtensions.AlreadyExists);
        if (saved.Failed)
        {
            await storage.DeleteAsync(KeptFolder.SavePrefix(name, head), ct);
            return raced ? new Result<bool>(false) : new Result<bool>(saved.Error);
        }

        foreach (string old in replaced)
        {
            await storage.DeleteAsync(KeptFolder.SavePrefix(name, old), ct);
        }

        return new Result<bool>(true);
    }

    // A tar of the history's bundles, `<n>.bundle`, oldest first; returns the first one missing, if any.
    private async Task<string?> WriteBundlesAsync(List<string> bundles, Stream stream, CancellationToken ct)
    {
        await using TarWriter tar = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: true);
        for (int n = 0; n < bundles.Count; n++)
        {
            await using Stream? bundle = await storage.OpenAsync(bundles[n], ct);
            if (bundle is null)
            {
                return bundles[n];
            }

            string entryName = string.Create(CultureInfo.InvariantCulture, $"{n}.bundle");
            await tar.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, entryName) { DataStream = bundle }, ct);
        }

        return null;
    }

    // The script's output: the folder's commit after the sync, and the history the kept folder lacks.
    // It comes from the nook, so the commit is checked like any input from outside.
    private static async Task<(string Head, byte[]? Bundle)> ReadAsync(Stream output, CancellationToken ct)
    {
        string head = string.Empty;
        byte[]? bundle = null;
        await using TarReader reader = new TarReader(output, leaveOpen: true);
        TarEntry? entry = await reader.GetNextEntryAsync(copyData: false, ct);
        while (entry is not null)
        {
            using MemoryStream content = new MemoryStream();
            if (entry.DataStream is not null)
            {
                await entry.DataStream.CopyToAsync(content, ct);
            }

            switch (entry.Name.TrimStart('.', '/'))
            {
                case "head":
                    string text = Encoding.UTF8.GetString(content.ToArray());
                    head = text.Length is 40 or 64 && text.All(char.IsAsciiHexDigitLower) ? text : string.Empty;
                    break;
                case "bundle":
                    bundle = content.ToArray();
                    break;
            }

            entry = await reader.GetNextEntryAsync(copyData: false, ct);
        }

        return (head, bundle);
    }

    private static Error Failed(string why)
    {
        return Error.Conflict("nooks.folder_sync_failed", "Syncing the folder failed: " + why);
    }
}
