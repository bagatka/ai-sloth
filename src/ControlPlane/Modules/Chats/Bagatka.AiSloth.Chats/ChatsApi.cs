using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Harness;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Harnesses;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Chats;

// The front door for every contract of the module. Each feature is a file in Features/. Streams that outlive a
// call use `databases`, never `db`.
internal sealed partial class ChatsApi(
    IDbContextFactory<ChatsDbContext> databases,
    IWorkspacesApi workspaces,
    INooksApi nooks,
    INookFilesApi nookFiles,
    IAgentAccountsApi accounts,
    ChatRunners runners,
    NookSetups setups,
    ChatSignals signals,
    ActiveInstance active,
    IProductEvents productEvents,
    TimeProvider time) : IChatsApi, IChatSettingsApi, IChatUsageApi, IChatHarnessesApi
{
    // Adds a message to be saved, and says whether it is the chat's first. A chat's first message ends
    // its draft in the same save, so a draft is never deleted with a message in it: when the draft went
    // meanwhile, the save conflicts.
    private static async Task<bool> AddMessageAsync(ChatsDbContext db, Message message, CancellationToken ct)
    {
        db.Messages.Add(message);
        Draft? draft = await db.Drafts.SingleOrDefaultAsync(found => found.ChatId == message.ChatId, ct);
        if (draft is not null)
        {
            db.Drafts.Remove(draft);
        }

        return draft is not null;
    }

    // The chat, if the actor may do at least `needed` with it: a chat is as open as its nook. Not found
    // when they may not see it, so nobody learns that it exists; forbidden when they may only read.
    private async Task<Result<Chat>> FindChatAsync(ChatsDbContext db, Actor actor, ChatId id, AccessLevel needed, CancellationToken ct)
    {
        Chat? chat = await db.Chats.AsNoTracking().SingleOrDefaultAsync(found => found.Id == id, ct);
        if (chat is null)
        {
            return new Result<Chat>(ChatsErrors.NotFound);
        }

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Nook(chat.NookId.Value), ct);
        if (access is null)
        {
            return new Result<Chat>(ChatsErrors.NotFound);
        }

        if (access < needed)
        {
            return new Result<Chat>(Error.Forbidden);
        }

        return new Result<Chat>(chat);
    }

    // The person acting, if they may do at least `needed` with the workspace: their own settings
    // there, such as harness state, need a person with access. Not found without access.
    private async Task<Result<UserId>> PersonInAsync(Actor actor, WorkspaceId workspaceId, AccessLevel needed, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<UserId>(Error.Forbidden);
        }

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(workspaceId), ct);
        if (access is null)
        {
            return new Result<UserId>(WorkspacesErrors.NotFound);
        }

        return access < needed ? new Result<UserId>(Error.Forbidden) : new Result<UserId>(user.UserId);
    }

    private static readonly Error UnusableAccount = Error.Validation("account", "Use the workspace's account or your own, of a kind the harness takes.");

    // The account, when the actor may use it in the workspace and the harness takes its kind.
    private async Task<Result<AgentAccountSummary>> AccountForAsync(Actor actor, WorkspaceId workspaceId, AgentAccountId id, HarnessProfile harness, CancellationToken ct)
    {
        Result<IReadOnlyList<AgentAccountSummary>> usable = await accounts.ListAsync(actor, workspaceId, ct);
        if (usable.Failed)
        {
            return new Result<AgentAccountSummary>(usable.Error);
        }

        AgentAccountSummary? account = usable.Output.FirstOrDefault(found => found.Id == id && harness.Accepts(AccountCredentials.KindOf(found.Kind)));
        return account is null ? new Result<AgentAccountSummary>(UnusableAccount) : new Result<AgentAccountSummary>(account);
    }

    // The harness a chat runs, with a model's ID it can be given and a thinking level of its own.
    private static Result<HarnessProfile> HarnessWith(string id, string model, string thinking)
    {
        Result<HarnessProfile> harness = FindHarness(id);
        if (harness.Failed)
        {
            return harness;
        }

        if (!HarnessProfile.IsModelId(model))
        {
            return new Result<HarnessProfile>(Error.Validation("model", "Must be a model's ID as its vendor writes it, such as openai/gpt-6-luna: letters, digits, and . _ - : / ~ @ + only."));
        }

        bool thinks = harness.Output.ThinkingLevels.Contains(thinking, StringComparer.Ordinal);
        string levels = harness.Output.Name + " thinks at " + string.Join(", ", harness.Output.ThinkingLevels) + ".";
        return thinks ? harness : new Result<HarnessProfile>(Error.Validation("thinking", levels));
    }

    private static Result<HarnessProfile> FindHarness(string id)
    {
        HarnessProfile? harness = HarnessProfiles.Find(id);
        return harness is null
            ? new Result<HarnessProfile>(Error.Validation("harness", "Unknown harness; see GET /harnesses."))
            : new Result<HarnessProfile>(harness);
    }

    // Enough of a first message for its first line's title.
    private const int FirstMessageStart = 400;

    private static async Task<ChatSummary> SummaryAsync(ChatsDbContext db, Chat chat, CancellationToken ct)
    {
        bool waiting = await db.Messages.AnyAsync(message => message.ChatId == chat.Id
            && (message.State == MessageState.New || message.State == MessageState.Queued || message.State == MessageState.Steering), ct);
        string? firstMessage = await db.Messages.Where(message => message.ChatId == chat.Id)
            .OrderBy(message => message.SentAt).ThenBy(message => message.Id)
            .Select(message => message.Text.Substring(0, FirstMessageStart))
            .FirstOrDefaultAsync(ct);
        return chat.ToSummary(waiting, firstMessage);
    }

    // A chat's runner saves the chat too; a save it made meanwhile, which shows as a conflict or as an
    // event already saved with the sequence number taken, is read again, this many times.
    private const int ChangeAttempts = 3;

    // Changes the chat as it is now, and saves the event that tells it when the change changed anything.
    private async Task<Result<Chat>> ChangeChatAsync(ChatsDbContext db, ChatId id, Func<Chat, bool> change, ChatEventBody told, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            Chat chat = await db.Chats.SingleAsync(found => found.Id == id, ct);
            bool changed = change(chat);
            if (changed)
            {
                db.Events.Add(chat.Record(told, time));
            }

            Result saved = await db.SaveAsync(ct);
            if (!saved.Failed)
            {
                return new Result<Chat>(chat);
            }

            bool changedMeanwhile = saved.Error == ModuleDbContextExtensions.ConcurrencyConflict || saved.Error == ModuleDbContextExtensions.AlreadyExists;
            if (!changedMeanwhile || attempt == ChangeAttempts)
            {
                return new Result<Chat>(saved.Error);
            }

            db.ChangeTracker.Clear();
        }
    }
}
