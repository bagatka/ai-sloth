using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    private static readonly SearchValues<char> FolderNameCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789-./");

    public async Task<Result> SyncFolderAsync(Actor actor, SyncFolder command, CancellationToken ct)
    {
        if (actor is not SystemActor)
        {
            return new Result(Error.Forbidden);
        }

        if (!IsAbsolute(command.Path) || !IsFolderName(command.Folder))
        {
            return new Result(Error.Validation("folder", "An absolute path, and a name of at most 200 lowercase letters, digits, -, ., and /."));
        }

        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);
        Result<Nook> nook = await FindNookAsync(db, actor, command.NookId, AccessLevel.Write, ct);
        if (nook.Failed)
        {
            return new Result(nook.Error);
        }

        Result<DaemonConnection> connected = await lifecycle.ConnectAsync(db, actor, command.NookId, ct);
        if (connected.Failed)
        {
            return new Result(connected.Error);
        }

        DaemonConnection connection = connected.Output;

        return await folders.SyncAsync(connection, command.Path, command.Folder, ct);
    }

    public async Task<Result<IReadOnlyList<KeptFolderSummary>>> ListFoldersAsync(Actor actor, string prefix, CancellationToken ct)
    {
        if (actor is not SystemActor)
        {
            return new Result<IReadOnlyList<KeptFolderSummary>>(Error.Forbidden);
        }

        if (!IsFolderName(prefix))
        {
            return new Result<IReadOnlyList<KeptFolderSummary>>(Error.Validation("prefix", "At most 200 lowercase letters, digits, -, ., and /."));
        }

        IReadOnlyList<KeptFolder> kept = await folders.ListAsync(prefix, ct);
        return new Result<IReadOnlyList<KeptFolderSummary>>([.. kept.Select(folder => folder.ToSummary())]);
    }

    public async Task<Result> DeleteFoldersAsync(Actor actor, string prefix, CancellationToken ct)
    {
        if (actor is not SystemActor)
        {
            return new Result(Error.Forbidden);
        }

        if (!IsFolderName(prefix))
        {
            return new Result(Error.Validation("prefix", "At most 200 lowercase letters, digits, -, ., and /."));
        }

        await folders.DeleteAsync(prefix, ct);
        return new Result(new Success());
    }

    private static bool IsFolderName(string name)
    {
        return name.Length is > 0 and <= KeptFolder.MaxNameLength && !name.AsSpan().ContainsAnyExcept(FolderNameCharacters) && !name.Contains("..", StringComparison.Ordinal);
    }
}
