using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
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
        int? found = await NookProcesses.ExitCodeAsync(_alice, chat.NookId, "grep", "-q", "hi from the fake model", "/work/hello.txt");

        Assert.Equal(["message-sent", "turn-started"], watch.SeenOfChat.Take(2).Select(seen => seen.Type), StringComparer.Ordinal);
        Assert.Equal(sent.Id.Value, watch.SeenOfChat[0].Event.GetProperty("messageId").GetGuid());
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

        await Api.ExpectAsync(_alice.SendPostAsync(PathOf(chat) + "/stop", new { }), HttpStatusCode.NoContent);
        JsonElement stopped = await watch.NextAsync("turn-ended");
        await SendAsync(chat, "say hello");
        JsonElement next = await watch.NextAsync("turn-ended");
        ChatSummary after = await Api.ReadAsync<ChatSummary>(_alice.SendGetAsync(PathOf(chat)), HttpStatusCode.OK);

        Assert.Equal("cancelled", stopped.GetProperty("stopReason").GetString());
        Assert.Equal("end_turn", next.GetProperty("stopReason").GetString());
        Assert.False(after.Working);
    }

    [Fact]
    public async Task Only_members_of_the_workspace_can_use_a_chat()
    {
        ChatSummary chat = await StartChatAsync();
        using HttpClient bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());

        Problem get = await Api.ProblemAsync(bob.SendGetAsync(PathOf(chat)), HttpStatusCode.NotFound);
        Problem send = await Api.ProblemAsync(bob.SendPostAsync(PathOf(chat) + "/messages", new { text = "hi" }), HttpStatusCode.NotFound);
        Problem watch = await Api.ProblemAsync(bob.SendGetAsync(PathOf(chat) + "/events"), HttpStatusCode.NotFound);
        Problem start = await Api.ProblemAsync(bob.SendPostAsync(ChatsPath(chat.WorkspaceId), new { provider = "docker", harness = "claude-code", account = chat.Account }), HttpStatusCode.NotFound);

        Assert.Equal(ChatsErrors.NotFound.Code, get.Code);
        Assert.Equal(ChatsErrors.NotFound.Code, send.Code);
        Assert.Equal(ChatsErrors.NotFound.Code, watch.Code);
        Assert.Equal(WorkspacesErrors.NotFound.Code, start.Code);
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

    // One chat, one nook, one agent: agents never work on each other's files.
    [Fact]
    public async Task Every_chat_creates_a_nook_of_its_own()
    {
        (WorkspaceSummary workspace, AgentAccountSummary account) = await WorkspaceWithAccountAsync();

        ChatSummary first = await StartChatAsync(workspace, account);
        ChatSummary second = await StartChatAsync(workspace, account);
        foreach (ChatSummary written in new[] { first, second })
        {
            await Api.ReadAsync<ChatMessage>(_alice.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/chats/{written.Id.Value}/messages"), new { text = "say hello" }), HttpStatusCode.OK);
        }

        NookSummary nook = await Api.ReadAsync<NookSummary>(_alice.SendGetAsync(NookPath(first.NookId)), HttpStatusCode.OK);
        Page<ChatSummary> chats = await Api.ReadAsync<Page<ChatSummary>>(_alice.SendGetAsync(ChatsPath(workspace.Id)), HttpStatusCode.OK);

        Assert.NotEqual(first.NookId, second.NookId);
        Assert.Equal("claude-code", nook.Harness);
        Assert.Equal([second.Id, first.Id], chats.Items.Select(chat => chat.Id));
    }

    [Fact]
    public async Task A_chat_needs_a_harness_that_takes_its_account_and_a_refused_one_leaves_no_nook()
    {
        (WorkspaceSummary workspace, AgentAccountSummary team) = await WorkspaceWithAccountAsync();

        Problem wrongAccount = await Api.ProblemAsync(
            _alice.SendPostAsync(ChatsPath(workspace.Id), new { provider = "docker", harness = "copilot", account = team.Id }), HttpStatusCode.BadRequest);
        Problem unknownHarness = await Api.ProblemAsync(
            _alice.SendPostAsync(ChatsPath(workspace.Id), new { provider = "docker", harness = "nowhere", account = team.Id }), HttpStatusCode.BadRequest);
        Page<NookSummary> nooks = await Api.ReadAsync<Page<NookSummary>>(_alice.SendGetAsync(NooksPath(workspace.Id)), HttpStatusCode.OK);

        Assert.True(wrongAccount.Errors?.ContainsKey("account"));
        Assert.True(unknownHarness.Errors?.ContainsKey("harness"));
        Assert.Empty(nooks.Items);
    }

    [Fact]
    public async Task A_copilot_chat_whose_token_is_refused_answers_with_the_reason()
    {
        (WorkspaceSummary workspace, _) = await WorkspaceWithAccountAsync();
        AgentAccountSummary own = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync("/agent-accounts", new { kind = "CopilotPlan", name = "Mine", secret = "github_pat_not_a_real_token" }), HttpStatusCode.Created);
        ChatSummary chat = await StartChatAsync(workspace, own, "copilot");
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);

        await SendAsync(chat, "hello");
        JsonElement ended = await watch.NextAsync("turn-ended");

        Assert.Equal("failed", ended.GetProperty("stopReason").GetString());
        Assert.StartsWith("The agent couldn't start", ended.GetProperty("failure").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task On_a_personal_account_the_owners_messages_reach_the_agent()
    {
        (WorkspaceSummary workspace, _) = await WorkspaceWithAccountAsync();
        AgentAccountSummary own = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync("/agent-accounts", new { kind = "AnthropicApiKey", name = "Mine", secret = FakeModel.ApiKey, endpoint = controlPlane.Model.Url }), HttpStatusCode.Created);
        ChatSummary chat = await StartChatAsync(workspace, own);
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);

        ChatMessage sent = await SendAsync(chat, "say hello");
        await watch.NextAsync("turn-ended");
        Problem noSuchProposal = await Api.ProblemAsync(_alice.SendPostAsync(PathOf(chat) + "/messages", new { text = "again", proposal = Guid.CreateVersion7() }), HttpStatusCode.BadRequest);

        Assert.Equal(chat.StartedBy, chat.AccountOwner);
        Assert.False(sent.IsProposal);
        Assert.Contains(watch.Seen, seen => string.Equals(seen.Type, "turn-started", StringComparison.Ordinal)
            && seen.Event.GetProperty("messageId").GetGuid() == sent.Id.Value);
        Assert.True(noSuchProposal.Errors?.ContainsKey("proposal"));
    }

    [Fact]
    public async Task A_message_from_someone_who_may_not_use_the_account_is_a_proposal_its_owner_sends_on()
    {
        (WorkspaceSummary workspace, _) = await WorkspaceWithAccountAsync();
        AgentAccountSummary own = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync("/agent-accounts", new { kind = "AnthropicApiKey", name = "Mine", secret = FakeModel.ApiKey, endpoint = controlPlane.Model.Url }), HttpStatusCode.Created);
        ChatSummary chat = await StartChatAsync(workspace, own);
        using HttpClient bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());
        string invites = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/invites");
        Invite invite = await Api.ReadAsync<Invite>(_alice.SendPostAsync(invites, new { access = "Write" }), HttpStatusCode.OK);
        await Api.ExpectAsync(bob.SendPostAsync("/invites/accept", new { code = invite.Code }), HttpStatusCode.OK);
        string messages = PathOf(chat) + "/messages";
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);

        ChatMessage proposal = await Api.ReadAsync<ChatMessage>(bob.SendPostAsync(messages, new { text = "Please write hello.txt for me" }), HttpStatusCode.OK);
        JsonElement proposed = await watch.NextAsync("message-proposed");
        await Api.ExpectAsync(bob.SendPostAsync(messages, new { text = "go", proposal = proposal.Id }), HttpStatusCode.Forbidden);
        ChatMessage sent = await Api.ReadAsync<ChatMessage>(_alice.SendPostAsync(messages, new { text = "Please write hello.txt for me, thanks", proposal = proposal.Id }), HttpStatusCode.OK);
        JsonElement messageSent = await watch.NextAsync("message-sent");
        JsonElement ended = await watch.NextAsync("turn-ended");

        Assert.True(proposal.IsProposal);
        Assert.Equal(proposal.SentBy.Value, proposed.GetProperty("proposedBy").GetGuid());
        Assert.False(sent.IsProposal);
        Assert.Equal(proposal.Id.Value, messageSent.GetProperty("proposal").GetGuid());
        Assert.Equal(["message-proposed", "message-sent", "turn-started"], watch.SeenOfChat.Take(3).Select(seen => seen.Type), StringComparer.Ordinal);
        Assert.Equal(sent.Id.Value, watch.SeenOfChat[2].Event.GetProperty("messageId").GetGuid());
        Assert.Equal("end_turn", ended.GetProperty("stopReason").GetString());
    }

    [Fact]
    public async Task A_removed_account_runs_no_more_agents()
    {
        ChatSummary chat = await StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(string.Create(CultureInfo.InvariantCulture, $"/agent-accounts/{chat.Account.Value}"), UriKind.Relative), Ct), HttpStatusCode.NoContent);

        await SendAsync(chat, "hello");
        JsonElement ended = await watch.NextAsync("turn-ended");

        Assert.Equal("failed", ended.GetProperty("stopReason").GetString());
        Assert.Equal("The agent couldn't start: Agent account not found.", ended.GetProperty("failure").GetString());
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    private static string PathOf(ChatSummary chat)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}");
    }

    private static string NooksPath(WorkspaceId workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Value}/nooks");
    }

    private static string NookPath(NookId nook)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/nooks/{nook.Value}");
    }

    private static string ChatsPath(WorkspaceId workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Value}/chats");
    }

    // A new workspace whose Anthropic account carries the fake model's key.
    private async Task<(WorkspaceSummary Workspace, AgentAccountSummary Account)> WorkspaceWithAccountAsync()
    {
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        string accounts = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/agent-accounts");
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(accounts, new { kind = "AnthropicApiKey", name = "Team key", secret = FakeModel.ApiKey, endpoint = controlPlane.Model.Url }), HttpStatusCode.Created);
        return (workspace, account);
    }

    private async Task<ChatSummary> StartChatAsync()
    {
        (WorkspaceSummary workspace, AgentAccountSummary account) = await WorkspaceWithAccountAsync();
        return await StartChatAsync(workspace, account);
    }

    private async Task<ChatSummary> StartChatAsync(WorkspaceSummary workspace, AgentAccountSummary account, string harness = "claude-code")
    {
        return await Api.ReadAsync<ChatSummary>(
            _alice.SendPostAsync(ChatsPath(workspace.Id), new { provider = "docker", harness, account = account.Id }), HttpStatusCode.Created);
    }

    private async Task<ChatMessage> SendAsync(ChatSummary chat, string text)
    {
        return await Api.ReadAsync<ChatMessage>(_alice.SendPostAsync(PathOf(chat) + "/messages", new { text }), HttpStatusCode.OK);
    }
}
