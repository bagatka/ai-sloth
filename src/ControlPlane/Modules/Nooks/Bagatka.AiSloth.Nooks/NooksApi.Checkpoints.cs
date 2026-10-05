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
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

// Checkpoints: the places a nook keeps (CheckpointPart) saved as snapshot commits, bundled into
// object storage, and put back in a nook or archived from them. The scripts own the format: a
// snapshot commit's tree is the place's files, its parents the repository's HEAD and branches, and
// its message names them and the remotes, so putting it back needs nothing else. Its author and
// dates are fixed, so the same files and branches make the same commit, which is how an unchanged
// place is noticed.
internal sealed partial class NooksApi
{
    // The most places one checkpoint keeps; more fails the checkpoint.
    private const int MaxPlaces = 100;

    // Arguments: the kept paths. Standard input: each place's commit at the last checkpoint, as
    // "<path>\t<commit>" lines. Standard output: a tar of `manifest`, one "<bundle or ->\t<commit>\t
    // <previous>\t<path>" line per place, and `bundles/<n>.bundle`.
    private const string SnapshotScript = """
        set -eu
        exec 3>&1 1>&2
        mkdir -p /var/lib/aisloth
        out=$(mktemp -d /var/lib/aisloth/checkpoint.XXXXXX)
        trap 'rm -rf "$out"' EXIT
        mkdir "$out/bundles"
        cat > "$out/previous"
        : > "$out/manifest"
        export GIT_AUTHOR_NAME=AiSloth GIT_AUTHOR_EMAIL=checkpoints@aisloth.invalid GIT_AUTHOR_DATE=1970-01-01T00:00:00Z
        export GIT_COMMITTER_NAME=AiSloth GIT_COMMITTER_EMAIL=checkpoints@aisloth.invalid GIT_COMMITTER_DATE=1970-01-01T00:00:00Z

        # keep PATH GIT_DIR WORK_TREE PATHSPEC...: commits the files at the pathspecs, as they are, with the
        # repository's branches and HEAD as parents and named in the message, without touching its index or
        # refs. The same commit as last time means nothing changed: no bundle. Otherwise the bundle holds
        # what the previous commit lacks, or everything when the nook no longer has it.
        keep() (
          path=$1
          export GIT_DIR="$2" GIT_WORK_TREE="$3" GIT_INDEX_FILE="$out/index"
          shift 3
          cd "$GIT_WORK_TREE"
          rm -f "$GIT_INDEX_FILE"
          if [ -f "$GIT_DIR/index" ]; then cp "$GIT_DIR/index" "$GIT_INDEX_FILE"; fi
          git add -A -- "$@"
          {
            printf 'AiSloth checkpoint\n\n'
            if [ "$GIT_DIR" = "$GIT_WORK_TREE/.git" ]; then
              if head=$(git symbolic-ref -q HEAD); then echo "HEAD ref: $head"; else echo "HEAD $(git rev-parse HEAD)"; fi
              git for-each-ref --format='%(objectname) %(refname)' refs/heads refs/remotes | grep -v '/HEAD$' || true
              git config --get-regexp '^remote\..*\.url$' | sed 's/^remote\.\(.*\)\.url /remote \1 /' || true
            fi
          } > "$out/message"
          parents=$({ git rev-parse -q --verify HEAD || true; git for-each-ref --format='%(objectname)' refs/heads refs/remotes; } | sort -u | sed 's/^/-p /')
          commit=$(git commit-tree $parents "$(git write-tree)" < "$out/message")
          previous=$(awk -F '\t' -v path="$path" '$1 == path { print $2 }' "$out/previous")
          if [ "$commit" = "$previous" ]; then
            printf -- '-\t%s\t\t%s\n' "$commit" "$path" >> "$out/manifest"
            return
          fi
          git cat-file -e "$previous^{commit}" 2>/dev/null || previous=""
          bundle=$(ls "$out/bundles" | wc -l)
          git update-ref refs/aisloth/checkpoint "$commit"
          git bundle create -q "$out/bundles/$bundle.bundle" refs/aisloth/checkpoint ${previous:+"^$previous"}
          git update-ref -d refs/aisloth/checkpoint
          printf '%s\t%s\t%s\t%s\n' "$bundle" "$commit" "$previous" "$path" >> "$out/manifest"
        )

        # The kept paths that exist, relative to /.
        kept=$#
        for path; do
          if [ -e "$path" ]; then set -- "$@" "${path#/}"; fi
        done
        shift "$kept"
        if [ $# -gt 0 ]; then
          [ -d /var/lib/aisloth/kept.git ] || git init -q --bare /var/lib/aisloth/kept.git
          keep / /var/lib/aisloth/kept.git / "$@"
        fi

        # Each repository directly in /work, then the rest of /work.
        set -- .
        for folder in /work/*/; do
          folder=${folder%/}
          if [ -d "$folder/.git" ]; then
            keep "$folder" "$folder/.git" "$folder" .
            set -- "$@" ":(exclude,literal)${folder#/work/}"
          fi
        done
        if [ -d /work/.git ]; then
          keep /work /work/.git /work "$@"
        else
          [ -d /var/lib/aisloth/work.git ] || git init -q --bare /var/lib/aisloth/work.git
          keep /work /var/lib/aisloth/work.git /work "$@"
        fi
        tar -cf - -C "$out" manifest bundles >&3
        """;

    private const string RestoreScript = """
        set -eu
        exec 3>&1 1>&2
        # $1: what to archive of /work as it was, such as "." or a source's folder; empty to put the files
        # back in this nook instead. Then each place: its path, its commit, and how many bundles bring it.
        # Standard input: a tar of the bundles, <place>-<n>.bundle, oldest first.
        archive=$1
        shift
        mkdir -p /var/lib/aisloth
        in=$(mktemp -d /var/lib/aisloth/restore.XXXXXX)
        trap 'rm -rf "$in"' EXIT
        tar -xf - -C "$in"
        if [ -n "$archive" ]; then
          root="$in/root"
        else
          # Nothing ran in the nook before its files come back, apart from an earlier try at this.
          root=""
          find /work -mindepth 1 -maxdepth 1 -exec rm -rf {} +
          rm -rf /var/lib/aisloth/work.git /var/lib/aisloth/kept.git
        fi
        place=0
        while [ $# -gt 0 ]; do
          path=$1 commit=$2 count=$3
          shift 3
          git init -q --bare "$in/git"
          n=0
          while [ "$n" -lt "$count" ]; do
            git --git-dir="$in/git" fetch -q "$in/$place-$n.bundle" +refs/aisloth/checkpoint:refs/aisloth/checkpoint
            n=$((n + 1))
          done
          git --git-dir="$in/git" update-ref -d refs/aisloth/checkpoint
          if git --git-dir="$in/git" cat-file commit "$commit" | grep -q '^HEAD '; then
            # A repository: its branches, remotes, and HEAD back, and its changes uncommitted.
            mkdir -p "$root$path"
            mv "$in/git" "$root$path/.git"
            (
              cd "$root$path"
              git config core.bare false
              git cat-file commit "$commit" | sed '1,/^$/d' | while read -r first second third; do
                case "$first" in
                  HEAD) if [ "$second" = "ref:" ]; then git symbolic-ref HEAD "$third"; else git update-ref --no-deref HEAD "$second"; fi ;;
                  remote) git remote add "$second" "$third" ;;
                  AiSloth | '') ;;
                  *) git update-ref "$second" "$first" ;;
                esac
              done
              git read-tree -u --reset "$commit"
              if git rev-parse -q --verify HEAD > /dev/null; then git read-tree HEAD; else git read-tree --empty; fi
            )
          else
            # Files without a repository of their own: the rest of /work, or the kept paths under /.
            if [ "$path" = / ]; then gitdir="$root/var/lib/aisloth/kept.git"; else gitdir="$root/var/lib/aisloth/work.git"; fi
            mkdir -p "$(dirname "$gitdir")" "$root$path"
            mv "$in/git" "$gitdir"
            GIT_DIR="$gitdir" GIT_WORK_TREE="$root$path" git read-tree -u --reset "$commit"
          fi
          place=$((place + 1))
        done
        if [ -n "$archive" ]; then
          tar -czf - -C "$root/work" "$archive" >&3
        fi
        """;

    // Before a nook falls asleep, its files are kept as a checkpoint if they changed since the latest, so
    // a sleep long enough to delete its sandbox loses nothing; agents' changes are kept after each turn
    // already. A nook whose files never arrived has nothing to keep.
    internal async Task<Result> CheckpointBeforeSleepAsync(NookId nookId, CancellationToken ct)
    {
        Nook? nook = await db.Nooks.SingleOrDefaultAsync(found => found.Id == nookId, ct);
        if (nook is not { SourcesReady: true })
        {
            return new Result(new Success());
        }

        DaemonConnection? connection = await ConnectionAsync(SystemActors.Processes, nookId, ct);
        if (connection is null)
        {
            return new Result(NooksErrors.NotReady);
        }

        Result<Checkpoint> kept = await SaveCheckpointAsync(nook, connection, "Before sleeping", onlyIfChanged: true, ct);
        return kept.Failed ? new Result(kept.Error) : new Result(new Success());
    }

    // Saves the nook's files as its next checkpoint, or, when only a change matters, returns the latest
    // if every place is as it was. Its sources must be in place, or the checkpoint would keep a nook
    // without them.
    private async Task<Result<Checkpoint>> SaveCheckpointAsync(Nook nook, DaemonConnection connection, string note, bool onlyIfChanged, CancellationToken ct)
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
        ProcessRun run = await RunAsync(connection, SnapshotScript, nook.KeptPaths, NoVariables, async (stream, token) => { await stream.WriteAsync(commits, token); }, taken, ct);
        if (!run.Succeeded)
        {
            return new Result<Checkpoint>(CheckpointFailed(run.Errors));
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

        return parts.Count > 0 ? new Result<List<CheckpointPart>>(parts) : new Result<List<CheckpointPart>>(CheckpointFailed("The nook's checkpoint had no places."));
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
                return new Result<List<CheckpointPart>>(CheckpointFailed("The nook's checkpoint was malformed, or kept more than 100 repositories."));
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
    private async Task<List<KeptPlace>?> PlacesAsync(NookId nookId, int number, CancellationToken ct)
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
    private async Task<ProcessRun> RestoreAsync(DaemonConnection connection, IReadOnlyList<KeptPlace> places, string archive, Stream? output, CancellationToken ct)
    {
        List<string> arguments = [archive];
        foreach (KeptPlace place in places)
        {
            arguments.AddRange([place.Path, place.Commit, place.Bundles.Count.ToString(CultureInfo.InvariantCulture)]);
        }

        string? missing = null;
        ProcessRun run = await RunAsync(
            connection,
            RestoreScript,
            arguments,
            NoVariables,
            async (stream, token) => { missing = await WriteBundlesAsync(places, stream, token); },
            output,
            ct);
        return missing is null ? run : new ProcessRun(ExitCode: -1, "The checkpoint's bundle " + missing + " is missing from object storage.");
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

    private static Error CheckpointFailed(string why)
    {
        return Error.Conflict("nooks.checkpoint_failed", "Taking a checkpoint of the nook failed: " + why);
    }

    // A place of a checkpoint, and the bundles that bring its commit, oldest first.
    private sealed record KeptPlace(string Path, string Commit, IReadOnlyList<string> Bundles);
}
