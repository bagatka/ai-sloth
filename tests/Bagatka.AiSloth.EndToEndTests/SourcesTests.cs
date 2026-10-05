using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Folders in and out: a chat's nook starts with the workspace's GitHub repositories, copied in with
/// the person's GitHub connection, and its changes leave as branches and pull requests, as a download,
/// or as another nook's start. GitHub is fake; git, the nooks, and the control plane are real.
/// </summary>
public sealed class SourcesTests(ControlPlane controlPlane) : IDisposable
{
    private readonly string _login = "ada-" + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[^8..];
    private readonly string _owner = "acme-" + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture)[^8..];
    private readonly HttpClient _ada = controlPlane.ClientFor("ada-" + Guid.CreateVersion7());

    private FakeGitHub GitHub => controlPlane.GitHub;

    [Fact]
    public async Task A_chat_starts_with_a_repository_and_its_changes_leave_as_a_pull_request()
    {
        ChatSummary chat = await ChatWithRepositoryAsync();

        int? copied = await NookProcesses.ExitCodeAsync(_ada, chat.NookId, "grep", "-q", "hello from api", "/work/api/README.md");
        int? guided = await NookProcesses.ExitCodeAsync(_ada, chat.NookId, "grep", "-q", "`api/`", "/work/AGENTS.md");
        await NookProcesses.ExitCodeAsync(_ada, chat.NookId, "sh", "-c", "echo hi > /work/api/CHANGE.md");
        PushedSource[] pushed = await Api.ReadAsync<PushedSource[]>(_ada.SendPostAsync(PathOf(chat) + "/push", new { pullRequest = true }), HttpStatusCode.OK);
        string branch = "aisloth/" + ShortId(chat);
        string? file = await GitHub.ReadAsync(_owner, "api", "show", branch + ":CHANGE.md");
        string? commit = await GitHub.ReadAsync(_owner, "api", "log", "-1", "--format=%an <%ae>%n%B", branch);
        IReadOnlyList<FakeGitHub.PullRequest> pulls = GitHub.PullRequests(_owner, "api");

        Assert.Equal(0, copied);
        Assert.Equal(0, guided);
        PushedSource api = Assert.Single(pushed);
        Assert.Equal(new PushedSource("api", branch, 1, api.BranchUrl, api.PullRequestUrl, Problem: null), api);
        Assert.Equal("hi", file);
        Assert.StartsWith("The " + _login + " <", commit, StringComparison.Ordinal);
        Assert.Contains(string.Create(CultureInfo.InvariantCulture, $"Co-authored-by: AiSloth <{FakeGitHub.BotId}+{FakeGitHub.AppSlug}[bot]@users.noreply.github.com>"), commit, StringComparison.Ordinal);
        FakeGitHub.PullRequest pull = Assert.Single(pulls);
        Assert.Equal((branch, "main", _login), (pull.Head, pull.Base, pull.OpenedBy));
        Assert.Equal(pull.Url, api.PullRequestUrl);
    }

    [Fact]
    public async Task A_push_never_touches_the_default_branch_or_undoes_others_commits_and_needs_write()
    {
        ChatSummary chat = await ChatWithRepositoryAsync();
        await NookProcesses.ExitCodeAsync(_ada, chat.NookId, "sh", "-c", "echo hi > /work/api/CHANGE.md");
        await GitHub.CommitElsewhereAsync(_owner, "api", "taken");
        string? main = await GitHub.ReadAsync(_owner, "api", "rev-parse", "main");
        using HttpClient bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());
        Invite invite = await Api.ReadAsync<Invite>(_ada.SendPostAsync(PathOf(chat.WorkspaceId) + "/invites", new { access = AccessLevel.Read }), HttpStatusCode.OK);
        await Api.ExpectAsync(bob.SendPostAsync("/invites/accept", new { code = invite.Code }), HttpStatusCode.OK);

        PushedSource[] toMain = await Api.ReadAsync<PushedSource[]>(_ada.SendPostAsync(PathOf(chat) + "/push", new { branch = "main" }), HttpStatusCode.OK);
        PushedSource[] toTaken = await Api.ReadAsync<PushedSource[]>(_ada.SendPostAsync(PathOf(chat) + "/push", new { branch = "taken" }), HttpStatusCode.OK);
        await Api.ExpectAsync(bob.SendPostAsync(PathOf(chat) + "/push", new { }), HttpStatusCode.Forbidden);
        string? mainAfter = await GitHub.ReadAsync(_owner, "api", "rev-parse", "main");
        string? takenAfter = await GitHub.ReadAsync(_owner, "api", "log", "-1", "--format=%s", "taken");

        Assert.Contains("default branch", Assert.Single(toMain).Problem, StringComparison.Ordinal);
        Assert.Equal(SourcesErrors.NotFastForward.Message, Assert.Single(toTaken).Problem);
        Assert.Equal(main, mainAfter);
        Assert.Equal("Their commit", takenAfter);
    }

    [Fact]
    public async Task A_chat_from_another_starts_with_a_copy_of_its_files_which_download_too()
    {
        ChatSummary first = await ChatWithRepositoryAsync();
        await NookProcesses.ExitCodeAsync(_ada, first.NookId, "sh", "-c", "echo hi > /work/api/CHANGE.md");
        AgentAccountSummary account = await FakeAccountAsync(first.WorkspaceId);

        ChatSummary second = await Api.ReadAsync<ChatSummary>(
            _ada.SendPostAsync(PathOf(first.WorkspaceId) + "/chats", new { provider = "docker", harness = "claude-code", account = account.Id, copyOf = first.Id }), HttpStatusCode.Created);
        int? copied = await NookProcesses.ExitCodeAsync(_ada, second.NookId, "grep", "-q", "hi", "/work/api/CHANGE.md");
        NookSummary firstNook = await Api.ReadAsync<NookSummary>(_ada.SendGetAsync(PathOf(first.NookId)), HttpStatusCode.OK);
        NookSummary secondNook = await Api.ReadAsync<NookSummary>(_ada.SendGetAsync(PathOf(second.NookId)), HttpStatusCode.OK);
        using HttpResponseMessage download = await _ada.GetAsync(new Uri(PathOf(second.NookId) + "/download?source=api", UriKind.Relative), TestContext.Current.CancellationToken);
        List<string> entries = await EntriesAsync(download);

        Assert.Equal(0, copied);
        Assert.Equal(firstNook.Sources, secondNook.Sources);
        Assert.Equal("application/gzip", download.Content.Headers.ContentType?.MediaType);
        Assert.Contains("api/CHANGE.md", entries, StringComparer.Ordinal);
        Assert.Contains("api/README.md", entries, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Git_settings_name_the_author_and_branch_and_can_leave_out_the_co_author()
    {
        GitSettings settings = await Api.ReadAsync<GitSettings>(
            _ada.SendPutAsync("/git-settings", new { author = new { name = "Ada Lovelace", email = "ada@example.com" }, committer = (object?)null, aiSlothCoAuthor = false, branchPrefix = "feature/" }),
            HttpStatusCode.OK);
        ChatSummary chat = await ChatWithRepositoryAsync();
        await NookProcesses.ExitCodeAsync(_ada, chat.NookId, "sh", "-c", "echo hi > /work/api/CHANGE.md");

        PushedSource[] pushed = await Api.ReadAsync<PushedSource[]>(_ada.SendPostAsync(PathOf(chat) + "/push", new { }), HttpStatusCode.OK);
        string? commit = await GitHub.ReadAsync(_owner, "api", "log", "-1", "--format=%an <%ae>|%cn <%ce>|%B", "feature/" + ShortId(chat));

        Assert.Equal(new GitIdentity("Ada Lovelace", "ada@example.com"), settings.Effective?.Committer);
        Assert.Null(settings.Effective?.CoAuthor);
        Assert.Equal("feature/" + ShortId(chat), Assert.Single(pushed).Branch);
        Assert.StartsWith("Ada Lovelace <ada@example.com>|Ada Lovelace <ada@example.com>|", commit, StringComparison.Ordinal);
        Assert.DoesNotContain("Co-authored-by", commit, StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _ada.Dispose();
    }

    private static string PathOf(ChatSummary chat)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}");
    }

    private static string PathOf(WorkspaceId workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Value}");
    }

    private static string PathOf(NookId nook)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/nooks/{nook.Value}");
    }

    private static string ShortId(ChatSummary chat)
    {
        return chat.Id.Value.ToString("N", CultureInfo.InvariantCulture)[^6..];
    }

    // Ada connects GitHub, her workspace adds the repository, and a chat starts with it.
    private async Task<ChatSummary> ChatWithRepositoryAsync()
    {
        await GitHub.CreateRepositoryAsync(_owner, "api", _login);
        GitHubConnectionStarted started = await Api.ReadAsync<GitHubConnectionStarted>(_ada.SendPostAsync("/github/connections", new { }), HttpStatusCode.OK);
        GitHub.Approve(started.UserCode, _login);
        GitHubConnectionProgress progress = await Api.ReadAsync<GitHubConnectionProgress>(
            _ada.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/github/connections/{started.Id.Value}/complete"), new { }), HttpStatusCode.OK);
        Assert.Equal(_login, progress.Account?.Login);

        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(_ada.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        RepositorySummary repository = await Api.ReadAsync<RepositorySummary>(_ada.SendPostAsync(PathOf(workspace.Id) + "/repositories", new { fullName = _owner + "/api" }), HttpStatusCode.Created);
        AgentAccountSummary account = await FakeAccountAsync(workspace.Id);
        return await Api.ReadAsync<ChatSummary>(
            _ada.SendPostAsync(PathOf(workspace.Id) + "/chats", new { provider = "docker", harness = "claude-code", account = account.Id, repositories = new[] { new { repository = repository.Id } } }),
            HttpStatusCode.Created);
    }

    private async Task<AgentAccountSummary> FakeAccountAsync(WorkspaceId workspace)
    {
        return await Api.ReadAsync<AgentAccountSummary>(
            _ada.SendPostAsync(PathOf(workspace) + "/agent-accounts", new { kind = "AnthropicApiKey", name = "Fake", secret = FakeModel.ApiKey, endpoint = controlPlane.Model.Url }), HttpStatusCode.Created);
    }

    // The file names in a gzipped tar archive.
    private static async Task<List<string>> EntriesAsync(HttpResponseMessage download)
    {
        await Api.ExpectAsync(download, HttpStatusCode.OK);
        await using Stream body = await download.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        await using GZipStream gzip = new GZipStream(body, CompressionMode.Decompress);
        await using TarReader tar = new TarReader(gzip);
        List<string> entries = [];
        TarEntry? entry = await tar.GetNextEntryAsync(copyData: false, TestContext.Current.CancellationToken);
        while (entry is not null)
        {
            entries.Add(entry.Name.TrimStart('.', '/'));
            entry = await tar.GetNextEntryAsync(copyData: false, TestContext.Current.CancellationToken);
        }

        return entries;
    }

    /// <summary>One source's push, as the chat's push answers it.</summary>
    internal sealed record PushedSource(string Source, string? Branch, int Commits, Uri? BranchUrl, Uri? PullRequestUrl, string? Problem);
}
