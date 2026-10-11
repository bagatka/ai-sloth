using Bagatka.AiSloth.Nooks.Data;
using System;
using System.Collections.Generic;
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
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

// What a nook's repositories changed since they were copied in, read from the nook with diff.sh, and
// how much that is. Reading changes and keeping them with a checkpoint both read them here.
internal sealed class SourceDiffs(NookProcesses processes)
{
    // Each copied-in source's changes, by name; one not yet copied in has none to show.
    public async Task<Result<List<SourceChanges>>> ReadAsync(NooksDbContext db, NookId nookId, DaemonConnection connection, CancellationToken ct)
    {
        List<SourceCopy> copies = await db.SourceCopies.AsNoTracking().Where(copy => copy.NookId == nookId).OrderBy(copy => copy.Name).ToListAsync(ct);
        string limit = SourceChanges.MaxPatch.ToString(CultureInfo.InvariantCulture);
        List<SourceChanges> changes = [];
        foreach (SourceCopy copy in copies)
        {
            if (copy is not { Branch: string branch, Commit: string commit })
            {
                continue;
            }

            using MemoryStream printed = new MemoryStream();
            ProcessRun diffed = await processes.RunAsync(connection, Scripts.Diff, [copy.Name, commit, limit], NookProcesses.NoVariables, input: null, ScriptOutput.Small(printed, SourceChanges.MaxPatch + 1), ct);
            if (!diffed.Succeeded)
            {
                return new Result<List<SourceChanges>>(Error.Conflict("nooks.changes_failed", "Reading " + copy.Name + "'s changes in the nook failed: " + diffed.Errors));
            }

            (string patch, bool truncated) = WholeFiles(printed.ToArray());
            changes.Add(new SourceChanges(copy.Name, copy.RepositoryId, branch, commit, patch, truncated));
        }

        return new Result<List<SourceChanges>>(changes);
    }

    // The files and lines the patches change: a file per "diff --git" section, and the lines its hunks
    // add and remove, after its "@@" lines, so header lines never count. A truncated patch counts what
    // it holds.
    public static ChangeCount Count(IEnumerable<SourceChanges> changes)
    {
        int files = 0;
        int additions = 0;
        int deletions = 0;
        foreach (SourceChanges source in changes)
        {
            bool inHunk = false;
            foreach (string line in source.Patch.Split('\n'))
            {
                if (line.StartsWith("diff --git ", StringComparison.Ordinal))
                {
                    files++;
                    inHunk = false;
                }
                else if (line.StartsWith("@@", StringComparison.Ordinal))
                {
                    inHunk = true;
                }
                else if (inHunk && line.StartsWith('+', StringComparison.Ordinal))
                {
                    additions++;
                }
                else if (inHunk && line.StartsWith('-', StringComparison.Ordinal))
                {
                    deletions++;
                }
            }
        }

        return new ChangeCount(files, additions, deletions);
    }

    // The patch as printed, or, when it ran past the most a patch comes to, up to the last file that
    // fits whole: each file's section starts with "diff --git".
    private static (string Patch, bool Truncated) WholeFiles(byte[] printed)
    {
        if (printed.Length <= SourceChanges.MaxPatch)
        {
            return (Encoding.UTF8.GetString(printed), false);
        }

        int next = printed.AsSpan(0, SourceChanges.MaxPatch).LastIndexOf("\ndiff --git "u8);
        return (next < 0 ? string.Empty : Encoding.UTF8.GetString(printed, 0, next + 1), true);
    }
}
