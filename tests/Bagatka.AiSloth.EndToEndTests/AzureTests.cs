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
/// Chats whose nooks run on Azure Container Apps Sandboxes, when asked for (<see cref="AzureControlPlane"/>):
/// the agent works and Docker runs in the nook, a nook nobody uses sleeps with its memory and wakes
/// with its agent still running, and one asleep for long comes back from its latest checkpoint.
/// </summary>
public sealed class AzureTests(AzureControlPlane azure) : IClassFixture<AzureControlPlane>, IDisposable
{
    // The first nook waits for Azure to read an image it hasn't seen.
    private static readonly TimeSpan FirstTurn = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Sleep = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan Eviction = TimeSpan.FromMinutes(3);

    private readonly HttpClient? _alice = azure.ControlPlane?.ClientFor("alice-" + Guid.CreateVersion7());

    [Fact]
    public async Task A_nook_on_Azure_runs_its_agent_and_Docker_and_sleeps_and_wakes_with_its_memory()
    {
        ChatSummary chat = await StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(Alice, chat);
        await SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("checkpoint-saved", FirstTurn);
        int? docker = await NookProcesses.ExitCodeAsync(Alice, chat.NookId, "docker", "run", "--rm", "busybox", "true");

        NookStatus asleep = await StatusAsync(chat, status => status is NookStatus.Paused, Sleep);
        await SendAsync(chat, "What was my first message?");
        await watch.NextAsync("turn-ended", Sleep);

        Assert.Equal(0, docker);
        Assert.Equal(NookStatus.Paused, asleep);
        Assert.DoesNotContain(watch.Seen, seen => string.Equals(seen.Type, "agent-restarted", StringComparison.Ordinal));
        Assert.Contains(watch.Seen, seen => string.Equals(seen.Type, "agent-update", StringComparison.Ordinal)
            && seen.Event.GetProperty("update").ToString().Contains("You first said: Please write hello.txt for me", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_nook_on_Azure_asleep_for_long_is_evicted_and_comes_back_from_its_latest_checkpoint()
    {
        ChatSummary chat = await StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(Alice, chat);
        await SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("checkpoint-saved", FirstTurn);
        await NookProcesses.ExitCodeAsync(Alice, chat.NookId, "touch", "/tmp/outside-the-checkpoint");

        NookStatus evicted = await StatusAsync(chat, status => status is NookStatus.Evicted, Eviction);
        await SendAsync(chat, "What was my first message?");
        JsonElement restarted = await watch.NextAsync("agent-restarted", FirstTurn);
        await watch.NextAsync("turn-ended");
        int? restored = await NookProcesses.ExitCodeAsync(Alice, chat.NookId, "grep", "-q", "hi from the fake model", "/work/hello.txt");
        int? fresh = await NookProcesses.ExitCodeAsync(Alice, chat.NookId, "test", "!", "-e", "/tmp/outside-the-checkpoint");

        Assert.Equal(NookStatus.Evicted, evicted);
        Assert.True(restarted.GetProperty("remembers").GetBoolean());
        Assert.Equal(0, restored);
        Assert.Equal(0, fresh);
    }

    public void Dispose()
    {
        _alice?.Dispose();
    }

    private HttpClient Alice
    {
        get
        {
            Assert.SkipWhen(_alice is null, "Set BAGATKA_AZURE_SANDBOXES_GROUP and BAGATKA_NGROK_ENV_FILE to run nooks on Azure.");
            return _alice;
        }
    }

    // The fixture owns the app, and with it the model.
    private FakeModel Model => azure.ControlPlane!.Model;

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
        return await Api.ReadAsync<NookSummary>(Alice.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/nooks/{chat.NookId.Value}")), HttpStatusCode.OK);
    }

    // A chat on Azure in a new workspace whose Anthropic account carries the fake model's key.
    private async Task<ChatSummary> StartChatAsync()
    {
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(Alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        string path = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}");
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(
            Alice.SendPostAsync(path + "/agent-accounts", new { kind = "AnthropicApiKey", name = "Team key", secret = FakeModel.ApiKey, endpoint = Model.Url }), HttpStatusCode.Created);
        return await Api.ReadAsync<ChatSummary>(
            Alice.SendPostAsync(path + "/chats", new { provider = "azure", harness = "claude-code", account = account.Id }), HttpStatusCode.Created);
    }

    private async Task SendAsync(ChatSummary chat, string text)
    {
        await Api.ReadAsync<ChatMessage>(Alice.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}/messages"), new { text }), HttpStatusCode.OK);
    }
}
