using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Nooks nobody uses fall asleep, and wake for their next use with an agent that remembers; a nook
/// asleep for long is evicted and comes back from its latest checkpoint. On Docker a sleeping nook
/// keeps its files, not its memory.
/// </summary>
public sealed class SleepTests(SleepyControlPlane sleepy) : IClassFixture<SleepyControlPlane>, IDisposable
{
    // Falling asleep takes the idle period, the sleeper's next pass, and Docker stopping the nook;
    // eviction the time asleep on top.
    private static readonly TimeSpan Sleep = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan Eviction = TimeSpan.FromMinutes(2);

    private readonly HttpClient _alice = sleepy.ControlPlane.ClientFor("alice-" + Guid.CreateVersion7());

    [Fact]
    public async Task A_nook_nobody_uses_falls_asleep_and_wakes_for_the_next_message_with_its_files_and_an_agent_that_remembers()
    {
        ChatSummary chat = await StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);
        await SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("checkpoint-saved");

        NookStatus asleep = await StatusAsync(chat, status => status is NookStatus.Stopped, Sleep);
        await SendAsync(chat, "What was my first message?");
        JsonElement restarted = await watch.NextAsync("agent-restarted", Sleep);
        await watch.NextAsync("turn-ended");
        int? kept = await NookProcesses.ExitCodeAsync(_alice, chat.NookId, "grep", "-q", "hi from the fake model", "/work/hello.txt");

        Assert.Equal(NookStatus.Stopped, asleep);
        Assert.True(restarted.GetProperty("remembers").GetBoolean());
        Assert.Contains(watch.Seen, seen => string.Equals(seen.Type, "agent-update", StringComparison.Ordinal)
            && seen.Event.GetProperty("update").ToString().Contains("You first said: Please write hello.txt for me", StringComparison.Ordinal));
        Assert.Equal(0, kept);
    }

    [Fact]
    public async Task A_nook_whose_agent_works_stays_awake_while_an_idle_one_falls_asleep()
    {
        ChatSummary idle = await StartChatAsync();
        ChatSummary busy = await StartChatAsync();
        await using ChatWatch idleWatch = await ChatWatch.OpenAsync(_alice, idle);
        await using ChatWatch busyWatch = await ChatWatch.OpenAsync(_alice, busy);
        await SendAsync(idle, "say hello");
        await idleWatch.NextAsync("checkpoint-saved");
        await SendAsync(busy, "wait for me");
        await Model.Holds.ReadAsync(TestContext.Current.CancellationToken);

        await StatusAsync(idle, status => status is NookStatus.Stopped, Sleep);
        NookSummary busyNook = await NookAsync(busy);
        Model.Release();
        await busyWatch.NextAsync("turn-ended");

        Assert.Equal(NookStatus.Running, busyNook.Status);
    }

    [Fact]
    public async Task Waking_a_nook_early_gives_it_compute_before_any_message()
    {
        ChatSummary chat = await StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);
        await SendAsync(chat, "say hello");
        await watch.NextAsync("checkpoint-saved");
        await StatusAsync(chat, status => status is NookStatus.Stopped, Sleep);

        await Api.ExpectAsync(_alice.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/nooks/{chat.NookId.Value}/wake"), new { }), HttpStatusCode.NoContent);
        NookStatus awake = await StatusAsync(chat, status => status is NookStatus.Running, Sleep);

        Assert.Equal(NookStatus.Running, awake);
    }

    [Fact]
    public async Task A_nook_asleep_for_long_is_evicted_and_comes_back_from_its_latest_checkpoint()
    {
        ChatSummary chat = await StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);
        await SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("checkpoint-saved");
        await NookProcesses.ExitCodeAsync(_alice, chat.NookId, "touch", "/tmp/outside-the-checkpoint");

        NookStatus evicted = await StatusAsync(chat, status => status is NookStatus.Evicted, Eviction);
        await SendAsync(chat, "What was my first message?");
        JsonElement restarted = await watch.NextAsync("agent-restarted", Sleep);
        await watch.NextAsync("turn-ended");
        int? restored = await NookProcesses.ExitCodeAsync(_alice, chat.NookId, "grep", "-q", "hi from the fake model", "/work/hello.txt");
        int? fresh = await NookProcesses.ExitCodeAsync(_alice, chat.NookId, "test", "!", "-e", "/tmp/outside-the-checkpoint");

        Assert.Equal(NookStatus.Evicted, evicted);
        Assert.True(restarted.GetProperty("remembers").GetBoolean());
        Assert.Equal(0, restored);
        Assert.Equal(0, fresh);
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    // The fixture owns the app, and with it the model.
    private FakeModel Model => sleepy.ControlPlane.Model;

    // The nook's status once it is one the test waits for.
    private async Task<NookStatus> StatusAsync(ChatSummary chat, Func<NookStatus, bool> awaited, TimeSpan patience)
    {
        long started = TimeProvider.System.GetTimestamp();
        NookSummary nook = await NookAsync(chat);
        while (!awaited(nook.Status) && TimeProvider.System.GetElapsedTime(started) < patience)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500), TestContext.Current.CancellationToken);
            nook = await NookAsync(chat);
        }

        return nook.Status;
    }

    private async Task<NookSummary> NookAsync(ChatSummary chat)
    {
        return await Api.ReadAsync<NookSummary>(_alice.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/nooks/{chat.NookId.Value}")), HttpStatusCode.OK);
    }

    // A chat in a new workspace whose Anthropic account carries the fake model's key.
    private async Task<ChatSummary> StartChatAsync()
    {
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        string path = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}");
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(path + "/agent-accounts", new { kind = "AnthropicApiKey", name = "Team key", secret = FakeModel.ApiKey, endpoint = Model.Url }), HttpStatusCode.Created);
        return await Api.ReadAsync<ChatSummary>(
            _alice.SendPostAsync(path + "/chats", new { provider = "docker", harness = "claude-code", account = account.Id }), HttpStatusCode.Created);
    }

    private async Task SendAsync(ChatSummary chat, string text)
    {
        await Api.ReadAsync<ChatMessage>(_alice.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}/messages"), new { text }), HttpStatusCode.OK);
    }
}
