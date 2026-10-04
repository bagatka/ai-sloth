using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Chats with the real agent, Claude Code through its ACP adapter, in a real nook. Only the model is
/// fake, reached through the real model gateway.
/// </summary>
public sealed class ChatsTests(ControlPlane controlPlane) : IDisposable
{
    private readonly HttpClient _alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_agent_works_on_a_message_and_everyone_sees_each_step()
    {
        ChatSummary chat = await StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);

        ChatMessage sent = await SendAsync(chat, "Please write hello.txt for me");
        JsonElement ended = await watch.NextAsync("turn-ended");
        int? found = await ExitCodeAsync(chat.NookId, "grep", "-q", "hi from the fake model", "/work/hello.txt");

        Assert.Equal(["message-sent", "turn-started"], watch.Seen.Take(2).Select(seen => seen.Type), StringComparer.Ordinal);
        Assert.Equal(sent.Id.Value, watch.Seen[0].Event.GetProperty("messageId").GetGuid());
        Assert.Contains(watch.Seen, seen => string.Equals(seen.Type, "agent-update", StringComparison.Ordinal)
            && string.Equals(seen.Event.GetProperty("update").GetProperty("sessionUpdate").GetString(), "tool_call", StringComparison.Ordinal));
        Assert.Equal("end_turn", ended.GetProperty("stopReason").GetString());
        Assert.Equal(0, found);
        Assert.All(controlPlane.Model.Credentials, credentials =>
        {
            Assert.Equal(FakeModel.ApiKey, credentials.ApiKey);
            Assert.Null(credentials.Authorization);
        });
    }

    [Fact]
    public async Task A_message_sent_while_the_agent_works_joins_the_running_turn()
    {
        ChatSummary chat = await StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);
        controlPlane.Model.ForgetHolds();
        await SendAsync(chat, "wait for me");
        await controlPlane.Model.Holds.ReadAsync(Ct);

        ChatMessage second = await SendAsync(chat, "and then say hello");
        JsonElement steered = await watch.NextAsync("message-steered");
        controlPlane.Model.Release();
        JsonElement ended = await watch.NextAsync("turn-ended");

        Assert.Equal(second.Id.Value, steered.GetProperty("messageId").GetGuid());
        Assert.Single(watch.Seen, seen => string.Equals(seen.Type, "turn-started", StringComparison.Ordinal));
        Assert.Equal("end_turn", ended.GetProperty("stopReason").GetString());
    }

    [Fact]
    public async Task Stopping_ends_the_turn_and_the_agent_takes_the_next_message()
    {
        ChatSummary chat = await StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);
        controlPlane.Model.ForgetHolds();
        await SendAsync(chat, "wait for me");
        await controlPlane.Model.Holds.ReadAsync(Ct);

        await Api.ExpectAsync(await _alice.SendPostAsync(PathOf(chat) + "/stop", new { }), HttpStatusCode.NoContent);
        JsonElement stopped = await watch.NextAsync("turn-ended");
        await SendAsync(chat, "say hello");
        JsonElement next = await watch.NextAsync("turn-ended");

        Assert.Equal("cancelled", stopped.GetProperty("stopReason").GetString());
        Assert.Equal("end_turn", next.GetProperty("stopReason").GetString());
        Assert.False((await Api.ReadAsync<ChatSummary>(await _alice.SendGetAsync(PathOf(chat)), HttpStatusCode.OK)).Working);
    }

    [Fact]
    public async Task Only_members_of_the_workspace_can_use_a_chat()
    {
        ChatSummary chat = await StartChatAsync();
        using HttpClient bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());

        Problem get = await Api.ProblemAsync(await bob.SendGetAsync(PathOf(chat)), HttpStatusCode.NotFound);
        Problem send = await Api.ProblemAsync(await bob.SendPostAsync(PathOf(chat) + "/messages", new { text = "hi" }), HttpStatusCode.NotFound);
        Problem watch = await Api.ProblemAsync(await bob.SendGetAsync(PathOf(chat) + "/events"), HttpStatusCode.NotFound);
        Problem start = await Api.ProblemAsync(await bob.SendPostAsync(NookChatsPath(chat.NookId), new { }), HttpStatusCode.NotFound);

        Assert.Equal(ChatsErrors.NotFound.Code, get.Code);
        Assert.Equal(ChatsErrors.NotFound.Code, send.Code);
        Assert.Equal(ChatsErrors.NotFound.Code, watch.Code);
        Assert.Equal(NooksErrors.NotFound.Code, start.Code);
    }

    [Fact]
    public async Task The_model_gateway_refuses_calls_without_a_chat_token()
    {
        using HttpClient caller = new HttpClient { BaseAddress = controlPlane.ModelGatewayUrl };
        using HttpRequestMessage withoutToken = new HttpRequestMessage(HttpMethod.Post, new Uri("v1/messages", UriKind.Relative));
        using HttpRequestMessage withAnyToken = new HttpRequestMessage(HttpMethod.Post, new Uri("v1/messages", UriKind.Relative));
        withAnyToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-chat-token");

        using HttpResponseMessage refused = await caller.SendAsync(withoutToken, Ct);
        using HttpResponseMessage alsoRefused = await caller.SendAsync(withAnyToken, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, alsoRefused.StatusCode);
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    private static string PathOf(ChatSummary chat)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}");
    }

    private static string NookChatsPath(NookId nook)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/nooks/{nook.Value}/chats");
    }

    private async Task<ChatSummary> StartChatAsync()
    {
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(await _alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        string nooks = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/nooks");
        NookSummary nook = await Api.ReadAsync<NookSummary>(await _alice.SendPostAsync(nooks, new { provider = "docker" }), HttpStatusCode.Created);
        return await Api.ReadAsync<ChatSummary>(await _alice.SendPostAsync(NookChatsPath(nook.Id), new { }), HttpStatusCode.Created);
    }

    private async Task<ChatMessage> SendAsync(ChatSummary chat, string text)
    {
        return await Api.ReadAsync<ChatMessage>(await _alice.SendPostAsync(PathOf(chat) + "/messages", new { text }), HttpStatusCode.OK);
    }

    private async Task<int?> ExitCodeAsync(NookId nook, string command, params string[] arguments)
    {
        string processes = string.Create(CultureInfo.InvariantCulture, $"/nooks/{nook.Value}/processes");
        ProcessSummary process = await Api.ReadAsync<ProcessSummary>(await _alice.SendPostAsync(processes, new { command, arguments }), HttpStatusCode.OK);
        ProcessSummary exited = await Api.EventuallyAsync(async () =>
            (await Api.ReadAsync<Page<ProcessSummary>>(await _alice.SendGetAsync(processes), HttpStatusCode.OK)).Items
                .SingleOrDefault(found => found.Id == process.Id && found.ExitCode is not null));
        return exited.ExitCode;
    }
}
