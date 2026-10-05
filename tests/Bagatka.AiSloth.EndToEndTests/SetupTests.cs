using System;
using System.Globalization;
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
/// Fast start: a chat's agent starts with the chat, after the project's own setup, which runs whenever
/// a nook gets the project's files, here a copy of another chat's; and preparing that setup, which an
/// agent writes and a fresh nook tests.
/// </summary>
public sealed class SetupTests(ControlPlane controlPlane) : IDisposable
{
    private readonly HttpClient _alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

    [Fact]
    public async Task A_projects_setups_then_its_resumes_run_in_their_folders_before_its_agent_starts()
    {
        ChatSummary first = await StartChatAsync(copyOf: null);
        await WriteAsync(first, "/work/.agents/setup", "#!/bin/sh\necho \"setup in $(pwd)\" >> /tmp/ran\n", "755");
        await WriteAsync(first, "/work/app/.agents/setup", "echo \"setup in $(pwd)\" >> /tmp/ran\n", "644");
        await WriteAsync(first, "/work/app/.agents/resume", "#!/bin/sh\necho \"resume in $(pwd)\" >> /tmp/ran\n", "755");

        ChatSummary second = await StartChatAsync(copyOf: first);
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, second);
        JsonElement started = await watch.NextAsync("setup-started");
        JsonElement ended = await watch.NextAsync("setup-ended");
        await SendAsync(second, "say hello");
        await watch.NextAsync("turn-ended");
        int? ran = await NookProcesses.ExitCodeAsync(_alice, second.NookId, "sh", "-c", "printf 'setup in /work\\nsetup in /work/app\\nresume in /work/app\\n' | cmp -s - /tmp/ran");

        Assert.Equal([".agents/setup", "app/.agents/setup", "app/.agents/resume"], started.GetProperty("scripts").EnumerateArray().Select(script => script.GetString() ?? string.Empty), StringComparer.Ordinal);
        Assert.Equal(0, ended.GetProperty("exitCode").GetInt32());
        Assert.Equal(JsonValueKind.Null, ended.GetProperty("output").ValueKind);
        Assert.Equal(0, ran);
        Assert.True(IndexOf(watch, "setup-ended") < IndexOf(watch, "turn-started"));
    }

    [Fact]
    public async Task A_failed_setup_shows_its_output_and_the_agent_starts_knowing_it()
    {
        ChatSummary first = await StartChatAsync(copyOf: null);
        await WriteAsync(first, "/work/.agents/setup", "#!/bin/sh\necho 'npm ERR! 404 Not Found: @acme/ui'\nexit 3\n", "755");

        ChatSummary second = await StartChatAsync(copyOf: first);
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, second);
        JsonElement ended = await watch.NextAsync("setup-ended");
        await SendAsync(second, "say hello");
        JsonElement turn = await watch.NextAsync("turn-ended");
        NookSetup setup = await Api.ReadAsync<NookSetup>(_alice.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/nooks/{second.NookId.Value}/setup")), HttpStatusCode.OK);

        Assert.Equal(3, ended.GetProperty("exitCode").GetInt32());
        Assert.Contains("npm ERR! 404 Not Found: @acme/ui", ended.GetProperty("output").GetString(), StringComparison.Ordinal);
        Assert.Contains("==> .agents/setup failed with exit code 3", ended.GetProperty("output").GetString(), StringComparison.Ordinal);
        Assert.Equal("end_turn", turn.GetProperty("stopReason").GetString());
        Assert.Equal([".agents/setup"], setup.Scripts);
        Assert.Equal(3, setup.Run?.ExitCode);
        Assert.Contains(controlPlane.Model.Requests, body => body.Contains("setup failed with exit code 3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_chats_agent_starts_before_its_first_message()
    {
        ChatSummary chat = await StartChatAsync(copyOf: null);

        ProcessSummary agent = await Api.EventuallyAsync(async () =>
        {
            Page<ProcessSummary> listed = await Api.ReadAsync<Page<ProcessSummary>>(
                _alice.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/nooks/{chat.NookId.Value}/processes")), HttpStatusCode.OK);
            return listed.Items.SingleOrDefault(process => process.Command is "harness");
        });

        Assert.Null(agent.ExitCode);
    }

    [Fact]
    public async Task Preparing_has_the_agent_write_a_setup_and_fix_it_until_it_works_in_a_fresh_nook()
    {
        ChatSummary chat = await StartChatAsync(copyOf: null);
        await WriteAsync(chat, "/opt/by-hand", "installed by hand", "644");
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);

        await Api.ReadAsync<ChatMessage>(_alice.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}/prepare"), new { }), HttpStatusCode.OK);
        JsonElement failed = await watch.NextAsync("setup-tested", TimeSpan.FromMinutes(3));
        JsonElement passed = await watch.NextAsync("setup-tested", TimeSpan.FromMinutes(3));
        Page<NookSummary> nooks = await Api.ReadAsync<Page<NookSummary>>(
            _alice.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/workspaces/{chat.WorkspaceId.Value}/nooks")), HttpStatusCode.OK);

        Assert.Equal((1, 1, true), (failed.GetProperty("test").GetInt32(), failed.GetProperty("exitCode").GetInt32(), failed.GetProperty("agentFixes").GetBoolean()));
        Assert.Contains("==> .agents/setup failed with exit code 1", failed.GetProperty("output").GetString(), StringComparison.Ordinal);
        Assert.Contains(watch.Seen, seen => string.Equals(seen.Type, "message-sent", StringComparison.Ordinal)
            && seen.Event.GetProperty("text").GetString()!.StartsWith("AiSloth ran the project's setup in a fresh nook", StringComparison.Ordinal));
        Assert.Equal((2, 0, false), (passed.GetProperty("test").GetInt32(), passed.GetProperty("exitCode").GetInt32(), passed.GetProperty("agentFixes").GetBoolean()));
        Assert.Equal(JsonValueKind.String, passed.GetProperty("again").ValueKind);
        Assert.All(nooks.Items.Where(nook => nook.Id != chat.NookId), nook => Assert.Equal(NookStatus.Deleting, nook.Status));
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    private static int IndexOf(ChatWatch watch, string type)
    {
        return watch.Seen.FindIndex(seen => string.Equals(seen.Type, type, StringComparison.Ordinal));
    }

    // A chat whose Anthropic account carries the fake model's key: in a new workspace, or beside the
    // chat it copies, with that chat's files.
    private async Task<ChatSummary> StartChatAsync(ChatSummary? copyOf)
    {
        WorkspaceId workspace;
        if (copyOf is null)
        {
            WorkspaceSummary created = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
            workspace = created.Id;
        }
        else
        {
            workspace = copyOf.WorkspaceId;
        }

        string path = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Value}");
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(path + "/agent-accounts", new { kind = "AnthropicApiKey", name = "Key " + Guid.CreateVersion7(), secret = FakeModel.ApiKey, endpoint = controlPlane.Model.Url }),
            HttpStatusCode.Created);
        return await Api.ReadAsync<ChatSummary>(
            _alice.SendPostAsync(path + "/chats", new { provider = "docker", harness = "claude-code", account = account.Id, copyOf = copyOf?.Id }), HttpStatusCode.Created);
    }

    private async Task WriteAsync(ChatSummary chat, string path, string content, string mode)
    {
        int? written = await NookProcesses.ExitCodeAsync(_alice, chat.NookId, "sh", "-c", "mkdir -p \"$(dirname \"$1\")\" && printf %s \"$2\" > \"$1\" && chmod \"$3\" \"$1\"", "sh", path, content, mode);
        Assert.Equal(0, written);
    }

    private async Task SendAsync(ChatSummary chat, string text)
    {
        await Api.ReadAsync<ChatMessage>(_alice.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}/messages"), new { text }), HttpStatusCode.OK);
    }
}
