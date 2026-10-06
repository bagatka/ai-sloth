using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    // The most paths one call names: what a script's arguments hold comfortably.
    private const int MaxPaths = 10;

    // Arguments: the paths. The ones that exist, relative to /, the same bytes for the same files.
    private const string CopyOutScript = """
        set -eu
        exec 3>&1 1>&2
        named=$#
        for path; do
          if [ -e "$path" ]; then set -- "$@" "${path#/}"; fi
        done
        shift "$named"
        if [ $# -eq 0 ]; then set -- --files-from=/dev/null; fi
        tar -cf - -C / --sort=name --mtime=@0 --owner=0 --group=0 --numeric-owner "$@" | gzip -n >&3
        """;

    // Arguments: the paths to remove first.
    private const string CopyInScript = """
        set -eu
        for path; do rm -rf -- "$path"; done
        tar -xzf - -C /
        """;

    public async Task<Result> CopyFilesOutAsync(Actor actor, CopyFilesOut command, Stream destination, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(actor, command.NookId, AccessLevel.Write, ct);
        if (nook.Failed)
        {
            return new Result(nook.Error);
        }

        if (command.Paths.Count > MaxPaths || !command.Paths.All(IsAbsolute))
        {
            return new Result(Error.Validation("paths", "At most 10 absolute paths, without . or .. parts."));
        }

        DaemonConnection? connection = await ConnectionAsync(actor, command.NookId, ct);
        if (connection is null)
        {
            return new Result(NooksErrors.NotReady);
        }

        ProcessRun copied = await RunAsync(connection, CopyOutScript, command.Paths, NoVariables, input: null, destination, ct);
        return copied.Succeeded
            ? new Result(new Success())
            : new Result(Error.Conflict("nooks.copy_failed", "Copying files out of the nook failed: " + copied.Errors));
    }

    public async Task<Result> CopyFilesInAsync(Actor actor, CopyFilesIn command, Stream archive, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(actor, command.NookId, AccessLevel.Write, ct);
        if (nook.Failed)
        {
            return new Result(nook.Error);
        }

        if (command.Replacing.Count > MaxPaths || !command.Replacing.All(IsAbsolute))
        {
            return new Result(Error.Validation("replacing", "At most 10 absolute paths, without . or .. parts."));
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

        await KeepReadyCopyAsync(nook.Output, ct);
        ProcessRun copied = await RunAsync(connection, CopyInScript, command.Replacing, NoVariables, async (stream, token) => { await archive.CopyToAsync(stream, token); }, output: null, ct);
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
