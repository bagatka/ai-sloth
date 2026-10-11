using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Harness;

// A nook's setup as chats work with it (Nooks runs it): following a run to its end, keeping the end of
// its output to show. What people see in a chat never names where files are in a nook.
internal sealed class NookSetups(INooksApi nooks)
{
    // How much of a failed setup's output a chat shows: its end, where the failure is.
    public const int MaxOutput = 2000;

    // Where a fix's patches wait in a nook while they're applied.
    private const string FixFolder = "/tmp/aisloth/setup-fix";

    // Brings a setup's fix, another nook's changes, into a nook with the same repositories, as the
    // actor: each repository's changes are applied to its copy here with git apply, all of them, or
    // none when one doesn't apply, as when this nook changed the same lines. Its own changes stay.
    public async Task<Result> BringFixAsync(Actor actor, NookId into, IReadOnlyList<SourceChanges> fix, IReadOnlyList<NookSource> here, CancellationToken ct)
    {
        List<(string Source, string Patch)> patches = [];
        foreach (SourceChanges changes in fix.Where(found => found.Patch.Length > 0))
        {
            NookSource? same = here.FirstOrDefault(found => found.Repository == changes.Repository);

            // Not handled: a fix too large to list whole, at 2 MB of changes, doesn't apply; the agent
            // can bring it in.
            if (same is null || changes.Truncated)
            {
                return new Result(ChatsErrors.SetupFixDoesNotApply);
            }

            patches.Add((same.Name, changes.Patch));
        }

        if (patches.Count == 0)
        {
            return new Result(new Success());
        }

        using MemoryStream archive = await ArchiveAsync(patches, ct);
        Result copied = await nooks.CopyFilesInAsync(actor, new CopyFilesIn(into, Replacing: [FixFolder]), archive, ct);
        if (copied.Failed)
        {
            return copied;
        }

        // Every patch is checked before any applies, so a fix comes whole or not at all.
        foreach (string[] pass in (string[][])[["apply", "--check"], ["apply"]])
        {
            foreach ((string source, _) in patches)
            {
                Result<int> applied = await GitAsync(actor, into, source, [.. pass, FixFolder + "/" + source + ".patch"], ct);
                if (applied.Failed)
                {
                    return new Result(applied.Error);
                }

                if (applied.Output != 0)
                {
                    return new Result(ChatsErrors.SetupFixDoesNotApply);
                }
            }
        }

        return new Result(new Success());
    }

    // Runs git in a repository of the nook to its end; returns its exit code.
    private async Task<Result<int>> GitAsync(Actor actor, NookId nook, string source, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        StartProcess git = new StartProcess(nook, "git", arguments, "/work/" + source, OutputRetention.Recent, new Dictionary<string, string>(StringComparer.Ordinal));
        Result<ProcessSummary> started = await nooks.StartProcessAsync(actor, git, ct);
        if (started.Failed)
        {
            return new Result<int>(started.Error);
        }

        SetupEnd ended = await FollowAsync(nook, started.Output.Id, ct);
        return new Result<int>(ended.ExitCode);
    }

    // The patches as files of FixFolder, one per repository, by its name.
    private static async Task<MemoryStream> ArchiveAsync(List<(string Source, string Patch)> patches, CancellationToken ct)
    {
        MemoryStream archive = new MemoryStream();
        await using (GZipStream zipped = new GZipStream(archive, CompressionLevel.Fastest, leaveOpen: true))
        await using (TarWriter tar = new TarWriter(zipped, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach ((string source, string patch) in patches)
            {
                using MemoryStream content = new MemoryStream(Encoding.UTF8.GetBytes(patch));
                PaxTarEntry entry = new PaxTarEntry(TarEntryType.RegularFile, FixFolder.TrimStart('/') + "/" + source + ".patch")
                {
                    DataStream = content,
                    Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                };
                await tar.WriteEntryAsync(entry, ct);
            }
        }

        archive.Position = 0;
        return archive;
    }

    // Follows a setup run to its end, keeping the end of its output. A watch that fails ends as a
    // failed run, saying why.
    public async Task<SetupEnd> FollowAsync(NookId nookId, ProcessId run, CancellationToken ct)
    {
        try
        {
            return await ReadAsync(nookId, run, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            return new SetupEnd(ProcessExited.Lost, "Following the setup failed: " + exception.Message);
        }
    }

    private async Task<SetupEnd> ReadAsync(NookId nookId, ProcessId run, CancellationToken ct)
    {
        Result<IAsyncEnumerable<ProcessEvent>> watch = await nooks.WatchProcessAsync(SystemActors.Harness, new WatchProcess(nookId, run, FromOffset: 0), ct);
        if (watch.Failed)
        {
            return new SetupEnd(ProcessExited.Lost, "Following the setup failed: " + watch.Error.Message);
        }

        List<byte> kept = [];
        await foreach (ProcessEvent processEvent in watch.Output.WithCancellation(ct))
        {
            switch (processEvent.Value)
            {
                case ProcessOutput printed:
                    kept.AddRange(printed.Data.Span);
                    kept.RemoveRange(0, Math.Max(0, kept.Count - (MaxOutput * 4)));
                    break;
                case ProcessExited exited:
                    return new SetupEnd(exited.ExitCode, Tail(Encoding.UTF8.GetString([.. kept])));
            }
        }

        return new SetupEnd(ProcessExited.Lost, "The setup's output ended before it did.");
    }

    // The end of a setup's output, from the start of a line.
    private static string Tail(string output)
    {
        string trimmed = output.TrimEnd();
        if (trimmed.Length <= MaxOutput)
        {
            return trimmed;
        }

        string end = trimmed[^MaxOutput..];
        int line = end.IndexOf('\n', StringComparison.Ordinal);
        return line < 0 ? end : end[(line + 1)..];
    }
}
