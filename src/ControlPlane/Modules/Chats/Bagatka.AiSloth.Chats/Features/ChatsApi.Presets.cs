using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Harnesses;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<IReadOnlyList<PresetSummary>>> ListPresetsAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        Result<UserId> person = await PersonInAsync(actor, workspaceId, AccessLevel.Read, ct);
        if (person.Failed)
        {
            return new Result<IReadOnlyList<PresetSummary>>(person.Error);
        }

        // A preset shows where its account works: a workspace's in it, a person's own everywhere.
        Result<IReadOnlyList<AgentAccountSummary>> usable = await accounts.ListAsync(actor, workspaceId, ct);
        if (usable.Failed)
        {
            return new Result<IReadOnlyList<PresetSummary>>(usable.Error);
        }

        List<AgentAccountId> accountIds = [.. usable.Output.Select(account => account.Id)];
        List<Preset> presets = await db.Presets.AsNoTracking()
            .Where(preset => preset.OwnerId == person.Output && accountIds.Contains(preset.AgentAccountId))
            .OrderBy(preset => preset.AddedAt)
            .ToListAsync(ct);
        return new Result<IReadOnlyList<PresetSummary>>([.. presets.Select(preset => preset.ToSummary())]);
    }

    public async Task<Result<PresetSummary>> AddPresetAsync(Actor actor, AddPreset command, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        // As starting a chat: only people, where they may write.
        Result<UserId> person = await PersonInAsync(actor, command.WorkspaceId, AccessLevel.Write, ct);
        if (person.Failed)
        {
            return new Result<PresetSummary>(person.Error);
        }

        Result<HarnessProfile> found = HarnessWith(command.Harness, command.Model);
        if (found.Failed)
        {
            return new Result<PresetSummary>(found.Error);
        }

        HarnessProfile harness = found.Output;

        Result<AgentAccountSummary> account = await AccountForAsync(actor, command.WorkspaceId, command.Account, harness, ct);
        if (account.Failed)
        {
            return new Result<PresetSummary>(account.Error);
        }

        Result<Preset> saved = Preset.Save(person.Output, command.Name, harness, command.Account, command.Model, time);
        if (saved.Failed)
        {
            return new Result<PresetSummary>(saved.Error);
        }

        // Not handled: two saves at once past the limit, or of the same preset; the unique index refuses
        // the second of the same, and one past the limit stays.
        int kept = await db.Presets.CountAsync(preset => preset.OwnerId == person.Output, ct);
        if (kept >= Preset.MaxPerPerson)
        {
            return new Result<PresetSummary>(ChatsErrors.TooManyPresets);
        }

        bool exists = await db.Presets.AnyAsync(preset => preset.OwnerId == person.Output && preset.Harness == harness.Id && preset.AgentAccountId == command.Account && preset.Model == command.Model, ct);
        if (exists)
        {
            return new Result<PresetSummary>(ChatsErrors.PresetExists);
        }

        db.Presets.Add(saved.Output);
        Result stored = await db.SaveAsync(ct);
        if (stored.Failed)
        {
            return new Result<PresetSummary>(stored.Error);
        }

        return new Result<PresetSummary>(saved.Output.ToSummary());
    }

    public async Task<Result<PresetSummary>> RenamePresetAsync(Actor actor, PresetId id, string name, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        Preset? preset = await OwnPresetAsync(db, actor, id, ct);
        if (preset is null)
        {
            return new Result<PresetSummary>(ChatsErrors.PresetNotFound);
        }

        Result renamed = preset.Rename(name);
        if (renamed.Failed)
        {
            return new Result<PresetSummary>(renamed.Error);
        }

        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<PresetSummary>(saved.Error);
        }

        return new Result<PresetSummary>(preset.ToSummary());
    }

    public async Task<Result> RemovePresetAsync(Actor actor, PresetId id, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        Preset? preset = await OwnPresetAsync(db, actor, id, ct);
        if (preset is null)
        {
            return new Result(ChatsErrors.PresetNotFound);
        }

        db.Presets.Remove(preset);
        return await db.SaveAsync(ct);
    }

    // The preset, when it is the actor's.
    private static async Task<Preset?> OwnPresetAsync(ChatsDbContext db, Actor actor, PresetId id, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return null;
        }

        return await db.Presets.SingleOrDefaultAsync(preset => preset.Id == id && preset.OwnerId == user.UserId, ct);
    }
}
