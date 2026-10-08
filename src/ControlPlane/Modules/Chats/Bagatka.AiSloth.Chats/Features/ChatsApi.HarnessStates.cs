using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Harness;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
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

        Result<IReadOnlyList<HarnessStateSummary>> own = await StatesAsync(workspaceId, person.Output, ct);
        if (own.Failed)
        {
            return own;
        }

        Result<IReadOnlyList<HarnessStateSummary>> shared = await StatesAsync(workspaceId, person: null, ct);
        return shared.Failed ? shared : new Result<IReadOnlyList<HarnessStateSummary>>([.. own.Output, .. shared.Output]);
    }

    public async Task<Result> ForgetHarnessStateAsync(Actor actor, ForgetHarnessState command, CancellationToken ct)
    {
        // The workspace's is everyone's who writes there, as they may direct the agents that keep it.
        Result<UserId> person = await PersonInAsync(actor, command.WorkspaceId, command.Shared ? AccessLevel.Write : AccessLevel.Read, ct);
        if (person.Failed)
        {
            return new Result(person.Error);
        }

        UserId? keptFor = command.Shared ? null : person.Output;
        return await nooks.DeleteFoldersAsync(SystemActors.Harness, HarnessStates.PrefixOf(command.WorkspaceId, keptFor, command.Harness), ct);
    }

    // The state kept for the person in the workspace, or the workspace's own for none, by harness.
    private async Task<Result<IReadOnlyList<HarnessStateSummary>>> StatesAsync(WorkspaceId workspaceId, UserId? person, CancellationToken ct)
    {
        string prefix = HarnessStates.PrefixOf(workspaceId, person, harness: null);
        Result<IReadOnlyList<KeptFolderSummary>> folders = await nooks.ListFoldersAsync(SystemActors.Harness, prefix, ct);
        if (folders.Failed)
        {
            return new Result<IReadOnlyList<HarnessStateSummary>>(folders.Error);
        }

        // Each folder names the nook that saved it last, whose chat people know.
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);
        List<NookId> nookIds = [.. folders.Output.Select(folder => folder.SavedBy)];
        Dictionary<NookId, ChatId?> chats = await db.Chats.AsNoTracking().Where(chat => nookIds.Contains(chat.NookId)).ToDictionaryAsync(chat => chat.NookId, chat => (ChatId?)chat.Id, ct);
        List<HarnessStateSummary> states = [.. folders.Output
            .GroupBy(folder => HarnessStates.HarnessOf(folder.Folder, prefix), System.StringComparer.Ordinal)
            .Select(harness => harness.MaxBy(folder => folder.SavedAt)! with { Bytes = harness.Sum(folder => folder.Bytes) })
            .Select(folder => new HarnessStateSummary(HarnessStates.HarnessOf(folder.Folder, prefix), Shared: person is null, folder.SavedAt, folder.Bytes, chats.GetValueOrDefault(folder.SavedBy)))];
        return new Result<IReadOnlyList<HarnessStateSummary>>(states);
    }
}
