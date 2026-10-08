using Bagatka.AiSloth.Nooks.Data;
using System;
using System.Collections.Generic;
using System.IO;
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
    // The exit code that says there is nothing beyond the base to send.
    private const int NoChanges = 3;

    public async Task<Result<ExportedChanges>> ExportChangesAsync(Actor actor, ExportChanges command, Stream destination, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Nook> nook = await FindNookAsync(db, actor, command.NookId, AccessLevel.Write, ct);
        if (nook.Failed)
        {
            return new Result<ExportedChanges>(nook.Error);
        }

        Result<DaemonConnection> ready = await ReadyAsync(db, actor, nook.Output, changes: false, ct);
        if (ready.Failed)
        {
            return new Result<ExportedChanges>(ready.Error);
        }

        DaemonConnection connection = ready.Output;

        SourceCopy? copy = await db.SourceCopies.AsNoTracking().SingleOrDefaultAsync(found => found.NookId == command.NookId && found.Name == command.Source, ct);
        if (copy is not { Branch: string branch, Commit: string commit })
        {
            return new Result<ExportedChanges>(NooksErrors.SourceNotFound);
        }

        // The pusher's identity for this commit, and their co-author line for the hook.
        Dictionary<string, string> environment = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_AUTHOR_NAME"] = command.Identity.Author.Name,
            ["GIT_AUTHOR_EMAIL"] = command.Identity.Author.Email,
            ["GIT_COMMITTER_NAME"] = command.Identity.Committer.Name,
            ["GIT_COMMITTER_EMAIL"] = command.Identity.Committer.Email,
            ["GIT_CONFIG_COUNT"] = "1",
            ["GIT_CONFIG_KEY_0"] = "aisloth.coauthor",
            ["GIT_CONFIG_VALUE_0"] = command.Identity.CoAuthor ?? string.Empty,
        };
        ProcessRun exported = await processes.RunAsync(connection, Scripts.Export, [copy.Name, command.Message, commit], environment, input: null, ScriptOutput.To(destination), ct);
        if (exported.ExitCode == NoChanges)
        {
            return new Result<ExportedChanges>(new ExportedChanges(copy.RepositoryId, branch, commit, HasChanges: false));
        }

        return exported.Succeeded
            ? new Result<ExportedChanges>(new ExportedChanges(copy.RepositoryId, branch, commit, HasChanges: true))
            : new Result<ExportedChanges>(Error.Conflict("nooks.export_failed", "Taking " + copy.Name + "'s changes out of the nook failed: " + exported.Errors));
    }
}
