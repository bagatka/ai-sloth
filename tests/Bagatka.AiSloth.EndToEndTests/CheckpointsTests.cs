using System;
using System.Formats.Tar;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Checkpoints of a chat's nook, taken after every turn: listed, downloaded, and what a nook comes
/// back from when its container is lost, with an agent that remembers the conversation.
/// </summary>
public sealed class CheckpointsTests(ControlPlane controlPlane) : IDisposable
{
    // Losing a nook is noticed on the reconciler's next pass, then a new one starts.
    private static readonly TimeSpan Recovery = TimeSpan.FromMinutes(3);

    private readonly HttpClient _alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

    [Fact]
    public async Task Each_turn_ends_with_a_checkpoint_whose_files_stay_as_they_were()
    {
        ChatSummary chat = await StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);

        await SendAsync(chat, "Please write hello.txt for me");
        JsonElement first = await watch.NextAsync("checkpoint-saved");
        await NookProcesses.ExitCodeAsync(_alice, chat.NookId, "sh", "-c", "echo changed > /work/hello.txt");
        await SendAsync(chat, "say hello");
        JsonElement second = await watch.NextAsync("checkpoint-saved");
        Page<CheckpointSummary> listed = await Api.ReadAsync<Page<CheckpointSummary>>(_alice.SendGetAsync(NookPath(chat) + "/checkpoints"), HttpStatusCode.OK);
        string? then = await HelloAsync(NookPath(chat) + "/download?checkpoint=1");
        string? now = await HelloAsync(NookPath(chat) + "/download?checkpoint=2");
        Problem missing = await Api.ProblemAsync(_alice.SendGetAsync(NookPath(chat) + "/download?checkpoint=3"), HttpStatusCode.NotFound);

        Assert.Equal(1, first.GetProperty("number").GetInt32());
        Assert.Equal(2, second.GetProperty("number").GetInt32());
        Assert.Equal([2, 1], listed.Items.Select(checkpoint => checkpoint.Number));
        Assert.Equal("Please write hello.txt for me", listed.Items[1].Note);
        Assert.Equal("hi from the fake model\n", then);
        Assert.Equal("changed\n", now);
        Assert.Equal(NooksErrors.CheckpointNotFound.Code, missing.Code);
    }

    [Fact]
    public async Task A_nook_whose_container_is_lost_comes_back_from_its_checkpoint_and_its_agent_remembers()
    {
        ChatSummary chat = await StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);
        await SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("checkpoint-saved");

        await controlPlane.LoseSandboxAsync(chat.NookId.Value);
        await SendAsync(chat, "What was my first message?");
        JsonElement restarted = await watch.NextAsync("agent-restarted", Recovery);
        await watch.NextAsync("turn-ended");
        int? restored = await NookProcesses.ExitCodeAsync(_alice, chat.NookId, "grep", "-q", "hi from the fake model", "/work/hello.txt");

        Assert.True(restarted.GetProperty("remembers").GetBoolean());
        Assert.Contains(watch.Seen, seen => string.Equals(seen.Type, "agent-update", StringComparison.Ordinal)
            && seen.Event.GetProperty("update").ToString().Contains("You first said: Please write hello.txt for me", StringComparison.Ordinal));
        Assert.Equal(0, restored);
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    private static string NookPath(ChatSummary chat)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/nooks/{chat.NookId.Value}");
    }

    // A chat in a new workspace whose Anthropic account carries the fake model's key.
    private async Task<ChatSummary> StartChatAsync()
    {
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(PathOf(workspace.Id) + "/agent-accounts", new { kind = "AnthropicApiKey", name = "Team key", secret = FakeModel.ApiKey, endpoint = controlPlane.Model.Url }), HttpStatusCode.Created);
        return await StartChatAsync(workspace.Id, account.Id);
    }

    private async Task<ChatSummary> StartChatAsync(WorkspaceId workspace, AgentAccountId account)
    {
        return await Api.ReadAsync<ChatSummary>(
            _alice.SendPostAsync(PathOf(workspace) + "/chats", new { provider = "docker", harness = "claude-code", account }), HttpStatusCode.Created);
    }

    private static string PathOf(WorkspaceId workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Value}");
    }

    private async Task SendAsync(ChatSummary chat, string text)
    {
        await Api.ReadAsync<ChatMessage>(_alice.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}/messages"), new { text }), HttpStatusCode.OK);
    }

    // hello.txt in a downloaded archive of /work, or null when it has none.
    private async Task<string?> HelloAsync(string download)
    {
        using HttpResponseMessage response = await _alice.GetAsync(new Uri(download, UriKind.Relative), TestContext.Current.CancellationToken);
        await Api.ExpectAsync(response, HttpStatusCode.OK);
        await using Stream body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        await using GZipStream gzip = new GZipStream(body, CompressionMode.Decompress);
        await using TarReader tar = new TarReader(gzip);
        TarEntry? entry = await tar.GetNextEntryAsync(copyData: false, TestContext.Current.CancellationToken);
        while (entry is not null)
        {
            if (entry.Name is "./hello.txt" && entry.DataStream is not null)
            {
                using StreamReader reader = new StreamReader(entry.DataStream);
                return await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            }

            entry = await tar.GetNextEntryAsync(copyData: false, TestContext.Current.CancellationToken);
        }

        return null;
    }
}
