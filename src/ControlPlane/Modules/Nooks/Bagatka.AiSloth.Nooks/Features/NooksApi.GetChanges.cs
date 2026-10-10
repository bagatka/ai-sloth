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
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<IReadOnlyList<SourceChanges>>> GetChangesAsync(Actor actor, NookId nookId, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Nook> nook = await FindNookAsync(db, actor, nookId, AccessLevel.Read, ct);
        if (nook.Failed)
        {
            return new Result<IReadOnlyList<SourceChanges>>(nook.Error);
        }

        Result<DaemonConnection> ready = await ReadyAsync(db, actor, nook.Output, changes: false, ct);
        if (ready.Failed)
        {
            return new Result<IReadOnlyList<SourceChanges>>(ready.Error);
        }

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
            ProcessRun diffed = await processes.RunAsync(ready.Output, Scripts.Diff, [copy.Name, commit, limit], NookProcesses.NoVariables, input: null, ScriptOutput.Small(printed, SourceChanges.MaxPatch + 1), ct);
            if (!diffed.Succeeded)
            {
                return new Result<IReadOnlyList<SourceChanges>>(Error.Conflict("nooks.changes_failed", "Reading " + copy.Name + "'s changes in the nook failed: " + diffed.Errors));
            }

            (string patch, bool truncated) = WholeFiles(printed.ToArray());
            changes.Add(new SourceChanges(copy.Name, copy.RepositoryId, branch, commit, patch, truncated));
        }

        return new Result<IReadOnlyList<SourceChanges>>(changes);
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
