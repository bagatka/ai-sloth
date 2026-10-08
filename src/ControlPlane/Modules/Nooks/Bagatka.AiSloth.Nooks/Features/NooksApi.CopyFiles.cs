using Bagatka.AiSloth.Nooks.Data;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    // The most paths one call names: what a script's arguments hold comfortably.
    private const int MaxPaths = 10;

    public async Task<Result> CopyFilesOutAsync(Actor actor, CopyFilesOut command, Stream destination, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Nook> nook = await FindNookToOperateAsync(db, actor, command.NookId, ct);
        if (nook.Failed)
        {
            return new Result(nook.Error);
        }

        if (command.Paths.Count > MaxPaths || !command.Paths.All(IsAbsolute))
        {
            return new Result(Error.Validation("paths", "At most 10 absolute paths, without . or .. parts."));
        }

        Result<DaemonConnection> connected = await lifecycle.ConnectAsync(db, actor, command.NookId, ct);
        if (connected.Failed)
        {
            return new Result(connected.Error);
        }

        DaemonConnection connection = connected.Output;

        ProcessRun copied = await processes.RunAsync(connection, Scripts.CopyOut, command.Paths, NookProcesses.NoVariables, input: null, ScriptOutput.To(destination), ct);
        return copied.Succeeded
            ? new Result(new Success())
            : new Result(Error.Conflict("nooks.copy_failed", "Copying files out of the nook failed: " + copied.Errors));
    }

    public async Task<Result> CopyFilesInAsync(Actor actor, CopyFilesIn command, Stream archive, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Nook> nook = await FindNookToOperateAsync(db, actor, command.NookId, ct);
        if (nook.Failed)
        {
            return new Result(nook.Error);
        }

        if (command.Replacing.Count > MaxPaths || !command.Replacing.All(IsAbsolute))
        {
            return new Result(Error.Validation("replacing", "At most 10 absolute paths, without . or .. parts."));
        }

        Result<DaemonConnection> ready = await ReadyAsync(db, actor, nook.Output, changes: true, ct);
        if (ready.Failed)
        {
            return new Result(ready.Error);
        }

        DaemonConnection connection = ready.Output;
        ProcessRun copied = await processes.RunAsync(connection, Scripts.CopyIn, command.Replacing, NookProcesses.NoVariables, async (stream, token) => { await archive.CopyToAsync(stream, token); }, output: null, ct);
        return copied.Succeeded
            ? new Result(new Success())
            : new Result(Error.Conflict("nooks.copy_failed", "Copying files into the nook failed: " + copied.Errors));
    }

    // An absolute path without empty, . or .. parts, which scripts take relative to /.
    private static bool IsAbsolute(string path)
    {
        string[] parts = path.Split('/');
        bool absolute = path.Length is > 1 and <= 256 && path[0] == '/' && !path.EndsWith('/', StringComparison.Ordinal);
        return absolute && parts.Skip(1).All(part => part is not ("" or "." or "..") && !part.Any(char.IsControl));
    }
}
