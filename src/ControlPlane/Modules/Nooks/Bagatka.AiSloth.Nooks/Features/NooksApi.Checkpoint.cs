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
        Result<Nook> nook = await FindNookAsync(actor, command.NookId, AccessLevel.Write, ct);
        if (nook.Failed)
        {
            return new Result<CheckpointSummary>(nook.Error);
        }

        if (command.Note.Length > Checkpoint.MaxNoteLength)
        {
            return new Result<CheckpointSummary>(Error.Validation("note", "At most 200 characters."));
        }

        DaemonConnection? connection = await ConnectionAsync(command.NookId, ct);
        if (connection is null)
        {
            return new Result<CheckpointSummary>(NooksErrors.NotReady);
        }

        Result prepared = await PrepareSourcesAsync(nook.Output, connection, ct);
        if (prepared.Failed)
        {
            return new Result<CheckpointSummary>(prepared.Error);
        }

        Result<Checkpoint> saved = await SaveCheckpointAsync(nook.Output, connection, command.Note, ct);
        return saved.Failed ? new Result<CheckpointSummary>(saved.Error) : new Result<CheckpointSummary>(saved.Output.ToSummary());
    }
}
