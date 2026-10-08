using Bagatka.AiSloth.Nooks.Data;
using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.ObjectStorage;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

// Checkpoints: the places a nook keeps (CheckpointPart) saved as snapshot commits, bundled into
// object storage, and put back in a nook or archived from them. The scripts (snapshot.sh,
// restore.sh) own the format: a snapshot commit's tree is the place's files, its parents the
// repository's HEAD and branches, and its message names them and the remotes, so putting it back
// needs nothing else. Its author and dates are fixed, so the same files and branches make the same
// commit, which is how an unchanged place is noticed. A checkpoint leaves out what git ignores, git's
// settings besides remotes, tags, and repositories deeper than directly in /work; checkpoints are
// kept until their nook is deleted.
internal sealed class Checkpoints(NookProcesses processes, IObjectStorage storage, FileLocks fileLocks, TimeProvider time)
{
    // The most places one checkpoint keeps; more fails the checkpoint.
    private const int MaxPlaces = 100;



    // Saves the nook's files as its next checkpoint, or, when only a change matters, returns the latest
    // if every place is as it was. Its sources must be in place, or the checkpoint would keep a nook
    // without them.
    public async Task<Result<Checkpoint>> SaveAsync(NooksDbContext db, Nook nook, DaemonConnection connection, string note, bool onlyIfChanged, CancellationToken ct)
    {
        using IDisposable held = await fileLocks.AcquireAsync(nook.Id, ct);
        Checkpoint? latest = await db.Checkpoints.AsNoTracking().Where(found => found.NookId == nook.Id).OrderByDescending(found => found.Number).FirstOrDefaultAsync(ct);
        List<CheckpointPart> previous = [];
        if (latest is not null)
        {
            previous = await db.CheckpointParts.AsNoTracking().Where(part => part.CheckpointId == latest.Id).ToListAsync(ct);
        }

        byte[] commits = Encoding.UTF8.GetBytes(string.Concat(previous.Select(part => part.Path + "\t" + part.Commit + "\n")));
        Checkpoint checkpoint = Checkpoint.Take(nook.Id, (latest?.Number ?? 0) + 1, note, time);
        string path = Path.GetTempFileName();
        await using FileStream taken = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        ProcessRun run = await processes.RunAsync(connection, Scripts.Snapshot, nook.KeptPaths, NookProcesses.NoVariables, async (stream, token) => { await stream.WriteAsync(commits, token); }, ScriptOutput.To(taken), ct);
        if (!run.Succeeded)
        {
            return new Result<Checkpoint>(Failed(run.Errors));
        }

        taken.Position = 0;
        Result<List<CheckpointPart>> stored = await StoreAsync(checkpoint, taken, ct);
        if (stored.Failed)
        {
            return new Result<Checkpoint>(stored.Error);
        }

        // A place that didn't change makes the same commit and stores no bundle.
        bool unchanged = stored.Output.Count == previous.Count
            && stored.Output.All(part => previous.Exists(earlier => string.Equals(earlier.Path, part.Path, StringComparison.Ordinal) && string.Equals(earlier.Commit, part.Commit, StringComparison.Ordinal)));
        if (onlyIfChanged && unchanged && latest is not null)
        {
            return new Result<Checkpoint>(latest);
        }

        db.Checkpoints.Add(checkpoint);
        db.CheckpointParts.AddRange(stored.Output);
        Result saved = await db.SaveAsync(ct);
        return saved.Failed ? new Result<Checkpoint>(saved.Error) : new Result<Checkpoint>(checkpoint);
    }

    // Stores the bundles the snapshot wrote, and reads its manifest into parts. It comes from the
    // nook, so it is checked like any input from outside.
    private async Task<Result<List<CheckpointPart>>> StoreAsync(Checkpoint checkpoint, Stream taken, CancellationToken ct)
    {
        List<CheckpointPart> parts = [];
        await using TarReader reader = new TarReader(taken, leaveOpen: true);
        TarEntry? entry = await reader.GetNextEntryAsync(copyData: false, ct);
        while (entry is not null)
        {
            if (entry.DataStream is null)
            {
                // A folder.
            }
            else if (entry.Name is "manifest")
            {
                using StreamReader manifest = new StreamReader(entry.DataStream, Encoding.UTF8);
                string text = await manifest.ReadToEndAsync(ct);
                Result<List<CheckpointPart>> read = ReadManifest(checkpoint, text);
                if (read.Failed)
                {
                    return read;
                }

                parts = read.Output;
            }
            else if (BundleName(entry.Name) is string bundle)
            {
                await storage.PutAsync(Checkpoint.ObjectKey(checkpoint.NookId, checkpoint.Number, bundle), entry.DataStream, ct);
            }

            entry = await reader.GetNextEntryAsync(copyData: false, ct);
        }

        return parts.Count > 0 ? new Result<List<CheckpointPart>>(parts) : new Result<List<CheckpointPart>>(Failed("The nook's checkpoint had no places."));
    }

    private static Result<List<CheckpointPart>> ReadManifest(Checkpoint checkpoint, string manifest)
    {
        List<CheckpointPart> parts = [];
        foreach (string line in manifest.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] fields = line.Split('\t', 4);
            bool valid = fields.Length == 4
                && (fields[0] is "-" || BundleName("bundles/" + fields[0] + ".bundle") is not null)
                && IsCommit(fields[1])
                && (fields[2].Length == 0 || IsCommit(fields[2]))
                && IsPlace(fields[3])
                && !parts.Exists(part => string.Equals(part.Path, fields[3], StringComparison.Ordinal));
            if (!valid || parts.Count == MaxPlaces)
            {
                // Not handled: folders whose names hold a tab or a line break, and more than 100 repositories.
                return new Result<List<CheckpointPart>>(Failed("The nook's checkpoint was malformed, or kept more than 100 repositories."));
            }

            string? objectKey = fields[0] is "-" ? null : Checkpoint.ObjectKey(checkpoint.NookId, checkpoint.Number, fields[0] + ".bundle");
            parts.Add(new CheckpointPart(checkpoint.Id, fields[3], fields[1], fields[2].Length == 0 ? null : fields[2], objectKey));
        }

        return new Result<List<CheckpointPart>>(parts);
    }

    // `bundles/<n>.bundle` as the bundle's name in object storage, `<n>.bundle`; null for anything else.
    private static string? BundleName(string entryName)
    {
        const string Folder = "bundles/";
        const string Extension = ".bundle";
        bool shaped = entryName.StartsWith(Folder, StringComparison.Ordinal) && entryName.EndsWith(Extension, StringComparison.Ordinal);
        string number = shaped ? entryName[Folder.Length..^Extension.Length] : string.Empty;
        return number.Length is > 0 and <= 4 && number.All(char.IsAsciiDigit) ? number + Extension : null;
    }

    private static bool IsCommit(string value)
    {
        return value.Length is 40 or 64 && value.All(char.IsAsciiHexDigitLower);
    }

    // `/`, `/work`, or a folder directly in it.
    private static bool IsPlace(string path)
    {
        const string Work = "/work/";
        if (path is "/" or "/work")
        {
            return true;
        }

        string folder = path.StartsWith(Work, StringComparison.Ordinal) ? path[Work.Length..] : string.Empty;
        return path.Length <= CheckpointPart.MaxPathLength
            && folder.Length > 0
            && folder is not ("." or "..")
            && !folder.Any(character => character == '/' || char.IsControl(character));
    }

    // A checkpoint's places, each with the bundles that bring its commit, oldest first; null when
    // the nook has no such checkpoint. Each commit comes from the first bundle that brought it.
    public static async Task<List<KeptPlace>?> PlacesAsync(NooksDbContext db, NookId nookId, int number, CancellationToken ct)
    {
        Checkpoint? checkpoint = await db.Checkpoints.AsNoTracking().SingleOrDefaultAsync(found => found.NookId == nookId && found.Number == number, ct);
        if (checkpoint is null)
        {
            return null;
        }

        List<CheckpointPart> parts = await db.CheckpointParts.AsNoTracking().Where(part => part.CheckpointId == checkpoint.Id).OrderBy(part => part.Path).ToListAsync(ct);
        List<CheckpointPart> bundled = await db.CheckpointParts.AsNoTracking()
            .Join(db.Checkpoints.Where(found => found.NookId == nookId && found.Number <= number), part => part.CheckpointId, found => found.Id, (part, found) => new { Part = part, found.Number })
            .Where(both => both.Part.ObjectKey != null)
            .OrderBy(both => both.Number)
            .Select(both => both.Part)
            .ToListAsync(ct);
        Dictionary<(string Path, string Commit), CheckpointPart> bringing = [];
        foreach (CheckpointPart part in bundled)
        {
            bringing.TryAdd((part.Path, part.Commit), part);
        }

        List<KeptPlace> places = [];
        foreach (CheckpointPart part in parts)
        {
            List<string> bundles = [];
            string? commit = part.Commit;
            while (commit is not null)
            {
                CheckpointPart? first = bringing.GetValueOrDefault((part.Path, commit));
                if (first is null)
                {
                    throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Checkpoint {number} of nook {nookId.Value} has no bundle for {part.Path}."));
                }

                bundles.Add(first.ObjectKey!);
                commit = first.Previous;
            }

            bundles.Reverse();
            places.Add(new KeptPlace(part.Path, part.Commit, bundles));
        }

        return places;
    }

    // Puts the places in the nook, `archive` empty, or writes an archive of them as /work was, with
    // `archive` the folder in it to archive, such as ".", to `output`.
    // Not handled: a nook whose disk can't hold the bundles and the files at once; restoring fails, and
    // streaming each bundle as it is fetched would take less.
    public async Task<ProcessRun> RestoreAsync(DaemonConnection connection, IReadOnlyList<KeptPlace> places, string archive, Stream? output, CancellationToken ct)
    {
        List<string> arguments = [archive];
        foreach (KeptPlace place in places)
        {
            arguments.AddRange([place.Path, place.Commit, place.Bundles.Count.ToString(CultureInfo.InvariantCulture)]);
        }

        string? missing = null;
        ProcessRun run = await processes.RunAsync(
            connection,
            Scripts.Restore,
            arguments,
            NookProcesses.NoVariables,
            async (stream, token) => { missing = await WriteBundlesAsync(places, stream, token); },
            output is null ? null : ScriptOutput.To(output),
            ct);
        return missing is null ? run : new ProcessRun(ExitCode: ProcessExited.Lost, "The checkpoint's bundle " + missing + " is missing from object storage.");
    }

    // A tar of every place's bundles, `<place>-<n>.bundle`; returns the first one missing, if any.
    private async Task<string?> WriteBundlesAsync(IReadOnlyList<KeptPlace> places, Stream stream, CancellationToken ct)
    {
        await using TarWriter tar = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: true);
        for (int place = 0; place < places.Count; place++)
        {
            for (int n = 0; n < places[place].Bundles.Count; n++)
            {
                string key = places[place].Bundles[n];
                await using Stream? bundle = await storage.OpenAsync(key, ct);
                if (bundle is null)
                {
                    return key;
                }

                string name = string.Create(CultureInfo.InvariantCulture, $"{place}-{n}.bundle");
                await tar.WriteEntryAsync(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = bundle }, ct);
            }
        }

        return null;
    }

    // Taking a checkpoint failed, for a reason the nook gave.
    public static Error Failed(string why)
    {
        return Error.Conflict("nooks.checkpoint_failed", "Taking a checkpoint of the nook failed: " + why);
    }
}
