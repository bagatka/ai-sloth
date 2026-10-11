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

internal sealed partial class ChatsApi
{
    public async Task<Result<ChatSummary>> StartAsync(Actor actor, StartChat command, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);

        // Only people start chats, and only where they may write.
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(command.WorkspaceId), ct);
        if (access is null)
        {
            return new Result<ChatSummary>(WorkspacesErrors.NotFound);
        }

        if (actor is not UserActor user || access < AccessLevel.Write)
        {
            return new Result<ChatSummary>(Error.Forbidden);
        }

        Result<(HarnessProfile Harness, AgentAccountCredential Account)> runsWith = await RunsWithAsync(actor, command, ct);
        if (runsWith.Failed)
        {
            return new Result<ChatSummary>(runsWith.Error);
        }

        (HarnessProfile harness, AgentAccountCredential account) = runsWith.Output;

        // A copy of another chat is of its nook's files; Nooks decides whether the actor may see them.
        NookId? copyOf = await db.Chats.AsNoTracking().Where(found => found.Id == command.CopyOf).Select(found => (NookId?)found.NookId).SingleOrDefaultAsync(ct);
        if (command.CopyOf is not null && copyOf is null)
        {
            return new Result<ChatSummary>(Error.Validation("copyOf", "Must be another chat of the workspace."));
        }

        // Every chat gets a nook of its own, so its agent never works on another agent's files. The nook
        // stands on its own: should saving the chat fail, it stays until someone deletes it. Its
        // checkpoints keep the agent's sessions, so a new agent can continue the conversation. Whoever
        // operates the nook can use what its agent can, so on a personal account it is reserved for
        // the account's owner.
        CreateNook nook = new CreateNook(command.WorkspaceId, command.Provider, harness.Id, command.Repositories, copyOf, command.Checkpoint, harness.SessionPaths, FromScratch: false, ReservedFor: account.OwnerId);
        Result<NookSummary> created = await nooks.CreateAsync(actor, nook, ct);
        if (created.Failed)
        {
            return new Result<ChatSummary>(created.Error);
        }

        Chat chat = Chat.Start(created.Output.Id, command.WorkspaceId, user.UserId, harness.Id, command.Model, command.Thinking, account, time);
        db.Chats.Add(chat);
        db.Drafts.Add(Draft.Of(chat));
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<ChatSummary>(saved.Error);
        }

        CaptureStarted(user, command, harness, account.Kind);

        // The agent starts now, after the nook's setup, while people write their first message.
        runners.Wake(chat.Id);
        return new Result<ChatSummary>(chat.ToSummary(messagesWaiting: false, firstMessage: null));
    }

    // What the chat's agent runs with: its harness (HarnessWith), and the account, when the actor may
    // use it and the harness takes its kind.
    private async Task<Result<(HarnessProfile Harness, AgentAccountCredential Account)>> RunsWithAsync(Actor actor, StartChat command, CancellationToken ct)
    {
        Result<HarnessProfile> harness = HarnessWith(command.Harness, command.Model, command.Thinking);
        if (harness.Failed)
        {
            return new Result<(HarnessProfile, AgentAccountCredential)>(harness.Error);
        }

        Result<AgentAccountCredential> account = await accounts.UseAsync(actor, command.Account, command.WorkspaceId, ct);
        if (account.Failed)
        {
            return new Result<(HarnessProfile, AgentAccountCredential)>(UnusableAccount);
        }

        bool takes = harness.Output.Accepts(AccountCredentials.KindOf(account.Output.Kind));
        return takes
            ? new Result<(HarnessProfile, AgentAccountCredential)>((harness.Output, account.Output))
            : new Result<(HarnessProfile, AgentAccountCredential)>(UnusableAccount);
    }

    // A chat started, for product analytics. It has no message yet: people who leave before writing
    // one show as starts without a first message_sent.
    private void CaptureStarted(UserActor user, StartChat command, HarnessProfile harness, AgentAccountKind accountKind)
    {
        productEvents.Capture(new ProductEvent("chat_started", user.UserId, command.WorkspaceId.Value, new Dictionary<string, ProductFact>(StringComparer.Ordinal)
        {
            ["harness"] = new ProductFact(harness.Id),
            ["model_chosen"] = new ProductFact(command.Model is not null),
            ["thinking"] = new ProductFact(command.Thinking ?? "default"),
            ["provider"] = new ProductFact(command.Provider),
            ["account_kind"] = new ProductFact(accountKind.ToString()),
            ["repositories"] = new ProductFact(command.Repositories.Count),
            ["copy"] = new ProductFact(command.CopyOf is not null),
            ["from_checkpoint"] = new ProductFact(command.Checkpoint is not null),
        }));
    }
}
