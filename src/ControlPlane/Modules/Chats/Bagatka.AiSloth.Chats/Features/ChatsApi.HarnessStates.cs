using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<IReadOnlyList<HarnessStateSummary>>> ListHarnessStatesAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct)
    {
        Result<UserId> person = await PersonInAsync(actor, workspaceId, AccessLevel.Read, ct);
        if (person.Failed)
        {
            return new Result<IReadOnlyList<HarnessStateSummary>>(person.Error);
        }

        List<HarnessState> states = await db.HarnessStates.AsNoTracking()
            .Where(state => state.PersonId == person.Output && state.WorkspaceId == workspaceId)
            .OrderBy(state => state.Harness)
            .ToListAsync(ct);
        return new Result<IReadOnlyList<HarnessStateSummary>>([.. states.Select(state => state.ToSummary())]);
    }

    public async Task<Result> ForgetHarnessStateAsync(Actor actor, WorkspaceId workspaceId, string harness, CancellationToken ct)
    {
        Result<UserId> person = await PersonInAsync(actor, workspaceId, AccessLevel.Read, ct);
        if (person.Failed)
        {
            return new Result(person.Error);
        }

        HarnessState? state = await db.HarnessStates.SingleOrDefaultAsync(
            found => found.PersonId == person.Output && found.WorkspaceId == workspaceId && found.Harness == harness, ct);
        if (state is null)
        {
            return new Result(new Success());
        }

        // The record goes first: without it, nothing reads the archive.
        db.HarnessStates.Remove(state);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return saved;
        }

        await storage.DeleteAsync(state.Folder, ct);
        return new Result(new Success());
    }
}
