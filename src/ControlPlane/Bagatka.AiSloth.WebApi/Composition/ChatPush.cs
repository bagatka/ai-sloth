using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.WebApi.Composition;

// Pushing a chat's changes: Nooks commits and bundles each source's changes, and Sources pushes
// them with the pusher's GitHub connection, onto one branch name across the repositories. Each
// repository is pushed on its own, so one refused leaves the others pushed; the outcome says which.
internal static class ChatPush
{
    internal sealed record PushedSource(string Source, string? Branch, int Commits, Uri? BranchUrl, Uri? PullRequestUrl, string? Problem);

    public static async Task<Result<IReadOnlyList<PushedSource>>> PushAsync(
        Actor actor,
        ChatId chatId,
        IReadOnlyList<string>? names,
        string? branch,
        bool pullRequest,
        string? message,
        IChatsApi chats,
        INooksApi nooks,
        ISourcesApi sources,
        IWorkspacesApi workspaces,
        IProductEvents productEvents,
        CancellationToken ct)
    {
        Result<ChatSummary> chat = await chats.GetAsync(actor, chatId, ct);
        if (chat.Failed)
        {
            return new Result<IReadOnlyList<PushedSource>>(chat.Error);
        }

        Result<NookSummary> nook = await nooks.GetAsync(actor, chat.Output.NookId, ct);
        if (nook.Failed)
        {
            return new Result<IReadOnlyList<PushedSource>>(nook.Error);
        }

        // Pushing is working in the nook: people who only see it may not.
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Nook(nook.Output.Id.Value), ct);
        if (actor is UserActor && access < AccessLevel.Write)
        {
            return new Result<IReadOnlyList<PushedSource>>(Error.Forbidden);
        }

        Result<GitSettings> settings = await sources.GetGitSettingsAsync(actor, ct);
        if (settings.Failed)
        {
            return new Result<IReadOnlyList<PushedSource>>(settings.Error);
        }

        if (settings.Output.Effective is not CommitIdentity identity)
        {
            return new Result<IReadOnlyList<PushedSource>>(SourcesErrors.GitHubNotConnected);
        }

        string shortId = chatId.Value.ToString("N", CultureInfo.InvariantCulture)[^6..];
        Push push = new Push(
            nook.Output.Id,
            branch ?? settings.Output.BranchPrefix + shortId,
            pullRequest,
            identity,
            message ?? "Changes from AiSloth chat " + shortId,
            "Pushed by AiSloth from chat `" + shortId + "`.");
        List<PushedSource> pushed = [];
        foreach (string name in names ?? [.. nook.Output.Sources.Select(found => found.Name)])
        {
            PushedSource outcome = await PushSourceAsync(actor, push, name, nooks, sources, ct);
            pushed.Add(outcome);
        }

        if (actor is UserActor person)
        {
            productEvents.Capture(new ProductEvent("changes_pushed", person.UserId, chat.Output.WorkspaceId.Value, new Dictionary<string, ProductFact>(StringComparer.Ordinal)
            {
                ["pull_request"] = new ProductFact(pullRequest),
                ["repositories"] = new ProductFact(pushed.Count),
                ["failed"] = new ProductFact(pushed.Count(source => source.Problem is not null)),
            }));
        }

        return new Result<IReadOnlyList<PushedSource>>(pushed);
    }

    // One repository's changes: taken out of the nook into a file of their own, then pushed.
    private static async Task<PushedSource> PushSourceAsync(Actor actor, Push push, string name, INooksApi nooks, ISourcesApi sources, CancellationToken ct)
    {
        string bundlePath = Path.GetTempFileName();
        await using FileStream bundle = new FileStream(bundlePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None, bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        Result<ExportedChanges> exported = await nooks.ExportChangesAsync(actor, new ExportChanges(push.NookId, name, push.Identity, push.Message), bundle, ct);
        if (exported.Failed)
        {
            return new PushedSource(name, Branch: null, 0, BranchUrl: null, PullRequestUrl: null, Describe(exported.Error));
        }

        if (!exported.Output.HasChanges)
        {
            return new PushedSource(name, Branch: null, 0, BranchUrl: null, PullRequestUrl: null, Problem: null);
        }

        bundle.Position = 0;
        PushChanges changes = new PushChanges(exported.Output.Repository, exported.Output.BaseBranch, exported.Output.BaseCommit, push.Branch, push.PullRequest, push.Description);
        Result<PushedChanges> delivered = await sources.PushAsync(actor, changes, bundle, ct);
        return delivered.Failed
            ? new PushedSource(name, push.Branch, 0, BranchUrl: null, PullRequestUrl: null, Describe(delivered.Error))
            : new PushedSource(name, delivered.Output.Branch, delivered.Output.Commits, delivered.Output.BranchUrl, delivered.Output.PullRequestUrl, Problem: null);
    }

    // What went wrong, in words for people: a validation error's are its fields'.
    private static string Describe(Error error)
    {
        return error.Fields.Count > 0 ? string.Join(" ", error.Fields.Select(field => field.Message)) : error.Message;
    }

    // What every repository's push shares.
    private sealed record Push(NookId NookId, string Branch, bool PullRequest, CommitIdentity Identity, string Message, string Description);
}
