using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    public async Task<Result> DownloadAsync(Actor actor, DownloadFiles command, Stream destination, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(actor, command.NookId, AccessLevel.Read, ct);
        if (nook.Failed)
        {
            return new Result(nook.Error);
        }

        bool known = await db.SourceCopies.AnyAsync(copy => copy.NookId == command.NookId && copy.Name == command.Source, ct);
        if (command.Source is not null && !known)
        {
            return new Result(NooksErrors.SourceNotFound);
        }

        // A checkpoint's /work: its repositories and the rest, or the one source.
        List<KeptPlace>? places = null;
        if (command.Checkpoint is int number)
        {
            places = await PlacesAsync(command.NookId, number, ct);
            if (places is null)
            {
                return new Result(NooksErrors.CheckpointNotFound);
            }

            places = command.Source is null
                ? [.. places.Where(place => place.Path is not "/")]
                : [.. places.Where(place => string.Equals(place.Path, "/work/" + command.Source, StringComparison.Ordinal))];
            if (places.Count == 0)
            {
                return new Result(NooksErrors.SourceNotFound);
            }
        }

        DaemonConnection? connection = await ConnectionAsync(actor, command.NookId, ct);
        if (connection is null)
        {
            return new Result(NooksErrors.NotReady);
        }

        Result prepared = await PrepareSourcesAsync(nook.Output, connection, ct);
        if (prepared.Failed)
        {
            return prepared;
        }

        ProcessRun archived;
        if (places is null)
        {
            archived = await RunAsync(connection, "tar -czf - -C /work \"$1\"", [command.Source ?? "."], NoVariables, input: null, destination, ct);
        }
        else
        {
            archived = await RestoreAsync(connection, places, command.Source ?? ".", destination, ct);
        }

        return archived.Succeeded
            ? new Result(new Success())
            : new Result(Error.Conflict("nooks.download_failed", "Archiving the nook's files failed: " + archived.Errors));
    }
}
