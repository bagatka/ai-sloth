using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    // What the agent of a chat that fixes another's setup is asked first, as whoever started it; AiSloth's
    // instructions tell it the setup failed and where its output is.
    private const string SetupFixRequest = "Fix the setup that failed, and commit the fix.";

    public async Task<Result<ChatSummary>> FixSetupAsync(Actor actor, ChatId id, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Chat> found = await FindChatAsync(db, actor, id, AccessLevel.Write, ct);
        if (found.Failed)
        {
            return new Result<ChatSummary>(found.Error);
        }

        if (actor is not UserActor user)
        {
            return new Result<ChatSummary>(Error.Forbidden);
        }

        if (found.Output.SetupExitCode is null or 0)
        {
            return new Result<ChatSummary>(ChatsErrors.SetupNotFailed);
        }

        Result<ChatSummary> started = await StartFixAsync(db, actor, found.Output, ct);
        if (started.Failed)
        {
            return started;
        }

        ChatId fix = started.Output.Id;
        Result<ChatMessage> sent = await SendAsync(actor, new SendMessage(fix, SetupFixRequest, Proposal: null), ct);
        if (sent.Failed)
        {
            return new Result<ChatSummary>(sent.Error);
        }

        // Not handled: a failure from here on leaves the new chat fixing the setup without running it
        // again after, or without the chats telling each other; people can still ask in it.
        Result asked = await RunSetupAgainAsync(actor, fix, ct);
        if (asked.Failed)
        {
            return new Result<ChatSummary>(asked.Error);
        }

        ChatEventBody told = new ChatEventBody(new SetupFixStarted(id, fix, user.UserId));
        Result<Chat> inFailed = await ChangeChatAsync(db, id, _ => true, told, ct);
        if (inFailed.Failed)
        {
            return new Result<ChatSummary>(inFailed.Error);
        }

        Result<Chat> inFix = await ChangeChatAsync(db, fix, _ => true, told, ct);
        if (inFix.Failed)
        {
            return new Result<ChatSummary>(inFix.Error);
        }

        signals.Notify(id);
        signals.Notify(fix);
        ChatSummary summary = await SummaryAsync(db, inFix.Output, ct);
        return new Result<ChatSummary>(summary);
    }

    // A chat like the one whose setup failed: the same repositories, harness, account, model, and
    // thinking, in a new nook on the same provider.
    private async Task<Result<ChatSummary>> StartFixAsync(ChatsDbContext db, Actor actor, Chat failed, CancellationToken ct)
    {
        Result<NookSummary> nook = await nooks.GetAsync(actor, failed.NookId, ct);
        if (nook.Failed)
        {
            return new Result<ChatSummary>(nook.Error);
        }

        ChatSummary like = await SummaryAsync(db, failed, ct);
        NookRepository[] repositories = [.. nook.Output.Sources.Select(source => new NookRepository(source.Repository, source.Branch))];
        StartChat start = new StartChat(failed.WorkspaceId, nook.Output.Provider, like.Harness, like.Account, like.Model, like.Thinking, repositories, CopyOf: null, Checkpoint: null);
        Result<ChatSummary> started = await StartAsync(actor, start, ct);
        return started;
    }
}
