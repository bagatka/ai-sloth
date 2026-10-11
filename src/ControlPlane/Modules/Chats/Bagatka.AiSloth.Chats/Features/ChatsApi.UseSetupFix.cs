using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result> UseSetupFixAsync(Actor actor, ChatId id, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Chat> found = await FindChatAsync(db, actor, id, AccessLevel.Write, ct);
        if (found.Failed)
        {
            return new Result(found.Error);
        }

        ChatId? fixingIn = await FixingInAsync(db, id, ct);
        if (fixingIn is not ChatId fix)
        {
            return new Result(ChatsErrors.NoSetupFix);
        }

        Chat? fixer = await db.Chats.AsNoTracking().SingleOrDefaultAsync(chat => chat.Id == fix, ct);
        if (fixer is not { SetupExitCode: 0 })
        {
            return new Result(ChatsErrors.SetupNotFixed);
        }

        Result<IReadOnlyList<SourceChanges>> changes = await nookFiles.GetChangesAsync(actor, fixer.NookId, ct);
        if (changes.Failed)
        {
            return new Result(changes.Error);
        }

        Result<NookSummary> nook = await nooks.GetAsync(actor, found.Output.NookId, ct);
        if (nook.Failed)
        {
            return new Result(nook.Error);
        }

        Result brought = await setups.BringFixAsync(actor, nook.Output.Id, changes.Output, nook.Output.Sources, ct);
        if (brought.Failed)
        {
            return brought;
        }

        Result again = await RunSetupAgainAsync(actor, id, ct);
        return again;
    }

    // The chat that fixes the chat's setup, the latest one started; null when none does.
    private static async Task<ChatId?> FixingInAsync(ChatsDbContext db, ChatId id, CancellationToken ct)
    {
        List<StoredEvent> started = await db.Events.AsNoTracking()
            .Where(found => found.ChatId == id && found.Kind == "setup-fix-started")
            .OrderByDescending(found => found.Sequence)
            .ToListAsync(ct);
        foreach (StoredEvent stored in started)
        {
            ChatEventBody body = stored.ToContract().Body;
            if (body is SetupFixStarted fix && fix.FailedIn == id)
            {
                return fix.FixingIn;
            }
        }

        return null;
    }
}
