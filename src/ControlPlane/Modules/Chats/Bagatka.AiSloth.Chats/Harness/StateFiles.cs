using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Harness;

// A harness state's files, by their paths relative to /, and how two copies of it merge. A state is
// compared by its manifest: one "<SHA-256> <path>" line per file, sorted, which a chat keeps for what
// its nook holds and whose own SHA-256 is the saved state's version. Memory is small text, so a
// state holds at most 1,000 files and 16 MiB.
internal sealed class StateFiles
{
    public const int MaxFiles = 1000;
    public const int MaxPathLength = 300;
    public const int MaxManifestLength = MaxFiles * (64 + 1 + MaxPathLength + 1);
    private const long MaxBytes = 16 * 1024 * 1024;

    private readonly SortedDictionary<string, byte[]> _files;

    private StateFiles(SortedDictionary<string, byte[]> files)
    {
        _files = files;
        Manifest = string.Concat(files.Select(file => Convert.ToHexStringLower(SHA256.HashData(file.Value)) + " " + file.Key + "\n"));
    }

    public static StateFiles Empty { get; } = new StateFiles(new SortedDictionary<string, byte[]>(StringComparer.Ordinal));

    public int Count => _files.Count;

    public string Manifest { get; }

    public byte[] Version => VersionOf(Manifest);

    public static byte[] VersionOf(string manifest)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(manifest));
    }

    // The regular files of a gzipped tar archive that are inside the state paths; a nook writes these,
    // so anything else in them is left out, and too much fails.
    public static async Task<Result<StateFiles>> ReadAsync(Stream archive, IReadOnlyList<string> statePaths, CancellationToken ct)
    {
        SortedDictionary<string, byte[]> files = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        long bytes = 0;
        await using GZipStream unzipped = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: true);
        await using TarReader tar = new TarReader(unzipped, leaveOpen: true);
        TarEntry? entry = await tar.GetNextEntryAsync(copyData: false, ct);
        while (entry is not null)
        {
            bool kept = entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile && entry.DataStream is not null && IsInside(entry.Name, statePaths);
            if (kept)
            {
                bytes += entry.Length;
                if (bytes > MaxBytes || files.Count == MaxFiles)
                {
                    // Not handled: harness state over 16 MiB or 1,000 files; it isn't kept between nooks.
                    return new Result<StateFiles>(Error.Conflict("chats.harness_state_too_big", "The harness state has more than 1,000 files or 16 MiB."));
                }

                using MemoryStream content = new MemoryStream();
                await entry.DataStream!.CopyToAsync(content, ct);
                files[entry.Name] = content.ToArray();
            }

            entry = await tar.GetNextEntryAsync(copyData: false, ct);
        }

        return new Result<StateFiles>(new StateFiles(files));
    }

    // A gzipped tar archive of the files, as Nooks unpacks it at /.
    public async Task<MemoryStream> ArchiveAsync(CancellationToken ct)
    {
        MemoryStream archive = new MemoryStream();
        await using (GZipStream zipped = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
        await using (TarWriter tar = new TarWriter(zipped, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (KeyValuePair<string, byte[]> file in _files)
            {
                using MemoryStream content = new MemoryStream(file.Value);
                PaxTarEntry entry = new PaxTarEntry(TarEntryType.RegularFile, file.Key)
                {
                    DataStream = content,
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead,
                };
                await tar.WriteEntryAsync(entry, ct);
            }
        }

        archive.Position = 0;
        return archive;
    }

    // Three-way, file by file, against the manifest of what the nook started from: a file only one
    // side changed, added, or deleted takes that side's; a file both changed keeps the lines of both,
    // theirs first, so nothing either learned is lost, at worst a line twice. A file one side deleted
    // and the other changed stays.
    public static StateFiles Merge(StateFiles ours, string? baseManifest, StateFiles theirs)
    {
        Dictionary<string, string> based = ParseManifest(baseManifest);
        SortedDictionary<string, byte[]> merged = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        foreach (string path in ours._files.Keys.Union(theirs._files.Keys, StringComparer.Ordinal).Union(based.Keys, StringComparer.Ordinal))
        {
            byte[]? mine = ours._files.GetValueOrDefault(path);
            byte[]? other = theirs._files.GetValueOrDefault(path);
            string? original = based.GetValueOrDefault(path);
            bool mineChanged = !string.Equals(HashOf(mine), original, StringComparison.Ordinal);
            bool otherChanged = !string.Equals(HashOf(other), original, StringComparison.Ordinal);
            byte[]? kept = (mineChanged, otherChanged) switch
            {
                (false, _) => other,
                (true, false) => mine,
                (true, true) => Both(mine, other),
            };
            if (kept is not null)
            {
                merged[path] = kept;
            }
        }

        return new StateFiles(merged);
    }

    private static byte[]? Both(byte[]? mine, byte[]? other)
    {
        if (mine is null || other is null)
        {
            return mine ?? other;
        }

        // Not handled: binary files both sides changed; ours wins.
        if (mine.AsSpan().SequenceEqual(other) || mine.Contains((byte)0) || other.Contains((byte)0))
        {
            return mine;
        }

        List<string> lines = [.. Lines(other)];
        HashSet<string> seen = new HashSet<string>(lines, StringComparer.Ordinal);
        foreach (string line in Lines(mine))
        {
            if (seen.Add(line))
            {
                lines.Add(line);
            }
        }

        return Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
    }

    private static string[] Lines(byte[] text)
    {
        return Encoding.UTF8.GetString(text).TrimEnd('\n').Split('\n');
    }

    private static string? HashOf(byte[]? content)
    {
        return content is null ? null : Convert.ToHexStringLower(SHA256.HashData(content));
    }

    private static Dictionary<string, string> ParseManifest(string? manifest)
    {
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (string line in (manifest ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            files[line[65..]] = line[..64];
        }

        return files;
    }

    private static bool IsInside(string name, IReadOnlyList<string> statePaths)
    {
        string path = "/" + name;
        bool plain = name.Length <= MaxPathLength && !name.Any(char.IsControl) && !name.Split('/').Any(part => part is "" or "." or "..");
        return plain && statePaths.Any(statePath => path.StartsWith(statePath + "/", StringComparison.Ordinal) || string.Equals(path, statePath, StringComparison.Ordinal));
    }
}
