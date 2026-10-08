using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: Ada's repository goes from GitHub into a chat and back as a pull request. With <c>sloth</c>
/// she connects GitHub, adds the repository, chats with it, pushes, and downloads it; a push never
/// touches the default branch or undoes a teammate's commits, viewers can't push, a copy of the chat
/// keeps its changes and repository, and her git settings name the author and branch. GitHub is fake; git, the nooks, and the control plane are real.
/// </summary>
public sealed class GitHubJourney(ControlPlane app) : IDisposable
{
    private readonly string _login = "ada-" + Guid.CreateVersion7();
    private readonly string _owner = "acme-" + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[^8..];
    private readonly HttpClient _bob = app.ClientFor("bob-" + Guid.CreateVersion7());
    private HttpClient? _ada;

    private HttpClient Ada => _ada!;

    private FakeGitHub GitHub => app.GitHub;

    [Fact]
    public async Task A_repository_goes_from_GitHub_into_a_chat_and_back_as_a_pull_request()
    {
        await app.GitHub.CreateRepositoryAsync(_owner, "api", _login);
        _ada = app.ClientFor(_login);
        ChatSummary chat = await SlothChatsWithTheRepositoryAndPushesItsChangesAsAPullRequestAsync();
        await APushNeverTouchesTheDefaultBranchOrATeammatesCommitsAndNeedsWriteAsync(chat);
        await AChatCopiedFromItStartsWithItsChangesAndRepositoryAsync(chat);
        await GitSettingsNameTheAuthorAndBranchAndCanLeaveOutTheCoAuthorAsync(chat);
    }

    public void Dispose()
    {
        _ada?.Dispose();
        _bob.Dispose();
    }

    private async Task<ChatSummary> SlothChatsWithTheRepositoryAndPushesItsChangesAsAPullRequestAsync()
    {
        await using SlothCli sloth = new SlothCli(_login);
        await sloth.RunAsync("host", "add", app.WebApiUrl.AbsoluteUri);
        await sloth.RunWithInputAsync(FakeModel.ApiKey + "\n", "account", "add", "anthropic-api-key", "--name", "Fake", "--endpoint", app.Model.Url.AbsoluteUri);

        int connected = await sloth.RunAsync("github", "connect");
        string connectedOutput = sloth.Output;
        int added = await sloth.RunAsync("repo", "add", _owner + "/api");
        int chatted = await sloth.RunAsync("chat", "Please write hello.txt for me", "--harness", "claude-code", "--repo", "api");
        string chatOutput = sloth.Output;
        ChatSummary chat = await SlothCli.NewestChatAsync(Ada);
        int? copied = await NookProcesses.ExitCodeAsync(Ada, chat.NookId, "grep", "-q", "hello from api", "/work/api/README.md");
        int? guided = await NookProcesses.ExitCodeAsync(Ada, chat.NookId, "grep", "-q", "`api/`", "/root/.claude/CLAUDE.md");
        await NookProcesses.ExitCodeAsync(Ada, chat.NookId, "sh", "-c", "echo hi > /work/api/CHANGE.md");
        int pushed = await sloth.RunAsync("chat", "push", ShortId(chat), "--pr");
        string pushedOutput = sloth.Output;
        string archive = Path.Combine(Path.GetTempPath(), "sloth-e2e-" + ShortId(chat) + ".tar.gz");
        int downloaded = await sloth.RunAsync("chat", "download", ShortId(chat), "--source", "api", "--out", archive);
        long archived = new FileInfo(archive).Length;
        File.Delete(archive);
        string branch = "aisloth/" + ShortId(chat);
        string? file = await GitHub.ReadAsync(_owner, "api", "show", branch + ":CHANGE.md");
        string? commit = await GitHub.ReadAsync(_owner, "api", "log", "-1", "--format=%an <%ae>%n%B", branch);

        Assert.Equal((0, 0, 0, 0, 0), (connected, added, chatted, pushed, downloaded));
        Assert.Contains("Connected GitHub as " + _login + ".", connectedOutput, StringComparison.Ordinal);
        Assert.Matches("^Chat [0-9a-f]{6} · Claude Code · Fake · cloud · api\n", chatOutput);
        Assert.Equal((0, 0), (copied, guided));
        Assert.Matches("^api: 1 commits on " + branch + "  http://127.0.0.1:[0-9]+/" + _owner + "/api/pull/1\n", pushedOutput);
        Assert.True(archived > 0);
        Assert.Equal("hi", file);
        Assert.StartsWith("The " + _login + " <", commit, StringComparison.Ordinal);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"Co-authored-by: AiSloth <{FakeGitHub.BotId}+{FakeGitHub.AppSlug}[bot]@users.noreply.github.com>"), commit, StringComparison.Ordinal);
        FakeGitHub.PullRequest pull = Assert.Single(GitHub.PullRequests(_owner, "api"));
        Assert.Equal((branch, "main", _login), (pull.Head, pull.Base, pull.OpenedBy));
        return chat;
    }

    private async Task APushNeverTouchesTheDefaultBranchOrATeammatesCommitsAndNeedsWriteAsync(ChatSummary chat)
    {
        await GitHub.CommitElsewhereAsync(_owner, "api", "taken");
        string? main = await GitHub.ReadAsync(_owner, "api", "rev-parse", "main");
        Invite invite = await Api.ReadAsync<Invite>(Ada.SendPostAsync(Paths.Workspace(chat.WorkspaceId) + "/invites", new { access = AccessLevel.Read }), HttpStatusCode.OK);
        await Api.ExpectAsync(_bob.SendPostAsync("/invites/accept", new { code = invite.Code }), HttpStatusCode.OK);

        PushedSource[] toMain = await Api.ReadAsync<PushedSource[]>(Ada.SendPostAsync(Paths.Chat(chat) + "/push", new { branch = "main" }), HttpStatusCode.OK);
        PushedSource[] toTaken = await Api.ReadAsync<PushedSource[]>(Ada.SendPostAsync(Paths.Chat(chat) + "/push", new { branch = "taken" }), HttpStatusCode.OK);
        await Api.ExpectAsync(_bob.SendPostAsync(Paths.Chat(chat) + "/push", new { }), HttpStatusCode.Forbidden);
        string? mainAfter = await GitHub.ReadAsync(_owner, "api", "rev-parse", "main");
        string? takenAfter = await GitHub.ReadAsync(_owner, "api", "log", "-1", "--format=%s", "taken");

        Assert.Contains("default branch", Assert.Single(toMain).Problem, StringComparison.Ordinal);
        Assert.Equal(SourcesErrors.NotFastForward.Message, Assert.Single(toTaken).Problem);
        Assert.Equal(main, mainAfter);
        Assert.Equal("Their commit", takenAfter);
    }

    private async Task AChatCopiedFromItStartsWithItsChangesAndRepositoryAsync(ChatSummary chat)
    {
        ChatSummary copy = await Api.ReadAsync<ChatSummary>(
            Ada.SendPostAsync(Paths.Workspace(chat.WorkspaceId) + "/chats", new { provider = "docker", harness = "claude-code", account = chat.Account, copyOf = chat.Id }), HttpStatusCode.Created);
        int? copied = await NookProcesses.ExitCodeAsync(Ada, copy.NookId, "grep", "-q", "hi", "/work/api/CHANGE.md");
        NookSummary original = await Api.ReadAsync<NookSummary>(Ada.SendGetAsync(Paths.Nook(chat.NookId)), HttpStatusCode.OK);
        NookSummary copyNook = await Api.ReadAsync<NookSummary>(Ada.SendGetAsync(Paths.Nook(copy.NookId)), HttpStatusCode.OK);

        Assert.Equal(0, copied);
        Assert.Equal(original.Sources, copyNook.Sources);
    }

    private async Task GitSettingsNameTheAuthorAndBranchAndCanLeaveOutTheCoAuthorAsync(ChatSummary chat)
    {
        GitSettings settings = await Api.ReadAsync<GitSettings>(
            Ada.SendPutAsync("/git-settings", new { author = new { name = "Ada Lovelace", email = "ada@example.com" }, committer = (object?)null, aiSlothCoAuthor = false, branchPrefix = "feature/" }),
            HttpStatusCode.OK);
        await NookProcesses.ExitCodeAsync(Ada, chat.NookId, "sh", "-c", "echo again > /work/api/CHANGE.md");

        PushedSource[] pushed = await Api.ReadAsync<PushedSource[]>(Ada.SendPostAsync(Paths.Chat(chat) + "/push", new { }), HttpStatusCode.OK);
        string? commit = await GitHub.ReadAsync(_owner, "api", "log", "-1", "--format=%an <%ae>|%cn <%ce>|%B", "feature/" + ShortId(chat));

        Assert.Equal(new GitIdentity("Ada Lovelace", "ada@example.com"), settings.Effective?.Committer);
        Assert.Null(settings.Effective?.CoAuthor);
        Assert.Equal("feature/" + ShortId(chat), Assert.Single(pushed).Branch);
        Assert.StartsWith("Ada Lovelace <ada@example.com>|Ada Lovelace <ada@example.com>|", commit, StringComparison.Ordinal);
        Assert.DoesNotContain("Co-authored-by", commit, StringComparison.Ordinal);
    }

    private static string ShortId(ChatSummary chat)
    {
        return chat.Id.Value.ToString("N", CultureInfo.InvariantCulture)[^6..];
    }

    /// <summary>One source's push, as the chat's push answers it.</summary>
    internal sealed record PushedSource(string Source, string? Branch, int Commits, Uri? BranchUrl, Uri? PullRequestUrl, string? Problem);
}
