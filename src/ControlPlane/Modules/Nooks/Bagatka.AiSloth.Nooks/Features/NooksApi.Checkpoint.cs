using Bagatka.AiSloth.Nooks.Data;
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
    public async Task<Result<CheckpointSummary>> CheckpointAsync(Actor actor, CheckpointNook command, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Nook> nook = await FindNookAsync(db, actor, command.NookId, AccessLevel.Write, ct);
        if (nook.Failed)
        {
            return new Result<CheckpointSummary>(nook.Error);
        }

        if (command.Note.Length > Checkpoint.MaxNoteLength)
        {
            return new Result<CheckpointSummary>(Error.Validation("note", "At most 200 characters."));
        }

        Result<DaemonConnection> ready = await ReadyAsync(db, actor, nook.Output, changes: false, ct);
        if (ready.Failed)
        {
            return new Result<CheckpointSummary>(ready.Error);
        }

        DaemonConnection connection = ready.Output;

        Result<Checkpoint> saved = await checkpoints.SaveAsync(db, nook.Output, connection, command.Note, onlyIfChanged: false, ct);
        return saved.Failed ? new Result<CheckpointSummary>(saved.Error) : new Result<CheckpointSummary>(saved.Output.ToSummary());
    }
}
