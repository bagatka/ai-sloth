using System;
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
/// Journey: only people who may use an account make its agent work. Alice's messages reach the agent
/// on her own account; Bob's become proposals she sends on; a chat needs a harness that takes its
/// account; a refused token, or a removed account, ends the turn with the reason; and the model gateway
/// answers only calls carrying a chat's token.
/// </summary>
public sealed class AgentPermissionsJourney(ControlPlane app) : IDisposable
{
    private readonly HttpClient _alice = app.ClientFor("alice-" + Guid.CreateVersion7());
    private readonly HttpClient _bob = app.ClientFor("bob-" + Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_people_who_may_use_an_account_make_its_agent_work()
    {
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, _alice);
        AgentAccountSummary own = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync("/agent-accounts", new { kind = "AnthropicApiKey", name = "Mine", secret = FakeModel.ApiKey, endpoint = app.Model.Url }), HttpStatusCode.Created);
        ChatSummary chat = await acme.StartChatAsync(account: own.Id);
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);
        await TheOwnersMessagesReachTheAgentAsync(acme, chat, watch);
        await SomeoneElsesMessageIsAProposalTheOwnerSendsOnAsync(acme, chat, watch);
        await AChatNeedsAHarnessThatTakesItsAccountAsync(acme);
        await ARefusedTokenEndsTheTurnWithTheReasonAsync(acme);
        await ARemovedAccountRunsNoMoreAgentsAsync(acme, chat, watch, own);
        await TheModelGatewayAnswersOnlyChatsAsync();
    }

    public void Dispose()
    {
        _alice.Dispose();
        _bob.Dispose();
    }

    private async Task TheOwnersMessagesReachTheAgentAsync(TestWorkspace acme, ChatSummary chat, ChatWatch watch)
    {
        ChatMessage sent = await acme.SendAsync(chat, "say hello");
        await watch.NextAsync("turn-ended");
        Problem noSuchProposal = await Api.ProblemAsync(_alice.SendPostAsync(Paths.Chat(chat) + "/messages", new { text = "again", proposal = Guid.CreateVersion7() }), HttpStatusCode.BadRequest);

        Assert.Equal(chat.StartedBy, chat.AccountOwner);
        Assert.False(sent.IsProposal);
        Assert.Contains(watch.Seen, seen => string.Equals(seen.Type, "turn-started", StringComparison.Ordinal) && seen.Event.GetProperty("messageId").GetGuid() == sent.Id.Value);
        Assert.True(noSuchProposal.Errors?.ContainsKey("proposal"));
    }

    private async Task SomeoneElsesMessageIsAProposalTheOwnerSendsOnAsync(TestWorkspace acme, ChatSummary chat, ChatWatch watch)
    {
        Invite invite = await Api.ReadAsync<Invite>(_alice.SendPostAsync(acme.Path + "/invites", new { access = "Write" }), HttpStatusCode.OK);
        await Api.ExpectAsync(_bob.SendPostAsync("/invites/accept", new { code = invite.Code }), HttpStatusCode.OK);
        string messages = Paths.Chat(chat) + "/messages";
        int seenBefore = watch.SeenOfChat.Count;

        ChatMessage proposal = await Api.ReadAsync<ChatMessage>(_bob.SendPostAsync(messages, new { text = "Please write hello.txt for me" }), HttpStatusCode.OK);
        JsonElement proposed = await watch.NextAsync("message-proposed");
        await Api.ExpectAsync(_bob.SendPostAsync(messages, new { text = "go", proposal = proposal.Id }), HttpStatusCode.Forbidden);
        ChatMessage sent = await Api.ReadAsync<ChatMessage>(_alice.SendPostAsync(messages, new { text = "Please write hello.txt for me, thanks", proposal = proposal.Id }), HttpStatusCode.OK);
        JsonElement messageSent = await watch.NextAsync("message-sent");
        JsonElement ended = await watch.NextAsync("turn-ended");

        Assert.True(proposal.IsProposal);
        Assert.Equal(proposal.SentBy.Value, proposed.GetProperty("proposedBy").GetGuid());
        Assert.False(sent.IsProposal);
        Assert.Equal(proposal.Id.Value, messageSent.GetProperty("proposal").GetGuid());
        Assert.Equal(
            ["message-proposed", "message-sent", "turn-started"],
            watch.SeenOfChat.Skip(seenBefore).Select(seen => seen.Type).SkipWhile(type => type is not "message-proposed").Take(3),
            StringComparer.Ordinal);
        Assert.Equal("end_turn", ended.GetProperty("stopReason").GetString());
    }

    private async Task AChatNeedsAHarnessThatTakesItsAccountAsync(TestWorkspace acme)
    {
        Page<NookSummary> before = await Api.ReadAsync<Page<NookSummary>>(_alice.SendGetAsync(acme.Path + "/nooks"), HttpStatusCode.OK);

        Problem wrongAccount = await Api.ProblemAsync(
            _alice.SendPostAsync(acme.Path + "/chats", new { provider = "docker", harness = "copilot", account = acme.Account }), HttpStatusCode.BadRequest);
        Problem unknownHarness = await Api.ProblemAsync(
            _alice.SendPostAsync(acme.Path + "/chats", new { provider = "docker", harness = "nowhere", account = acme.Account }), HttpStatusCode.BadRequest);
        Page<NookSummary> after = await Api.ReadAsync<Page<NookSummary>>(_alice.SendGetAsync(acme.Path + "/nooks"), HttpStatusCode.OK);

        Assert.True(wrongAccount.Errors?.ContainsKey("account"));
        Assert.True(unknownHarness.Errors?.ContainsKey("harness"));
        Assert.Equal(before.Items.Count, after.Items.Count);
    }

    private async Task ARefusedTokenEndsTheTurnWithTheReasonAsync(TestWorkspace acme)
    {
        AgentAccountSummary copilot = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync("/agent-accounts", new { kind = "CopilotPlan", name = "My Copilot", secret = "github_pat_not_a_real_token" }), HttpStatusCode.Created);
        ChatSummary chat = await acme.StartChatAsync("copilot", copilot.Id);
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);

        await acme.SendAsync(chat, "hello");
        JsonElement ended = await watch.NextAsync("turn-ended");

        Assert.Equal("failed", ended.GetProperty("stopReason").GetString());
        Assert.StartsWith("The agent couldn't start", ended.GetProperty("failure").GetString(), StringComparison.Ordinal);
    }

    // The chat's agent runs when its account goes: its next turn fails at once, and so does every one
    // after, whose agent can't start.
    private async Task ARemovedAccountRunsNoMoreAgentsAsync(TestWorkspace acme, ChatSummary chat, ChatWatch watch, AgentAccountSummary own)
    {
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(Paths.Account(own.Id), UriKind.Relative), Ct), HttpStatusCode.NoContent);

        await acme.SendAsync(chat, "hello");
        JsonElement next = await watch.NextAsync("turn-ended");
        await acme.SendAsync(chat, "hello again");
        JsonElement after = await watch.NextAsync("turn-ended");

        Assert.All([next, after], ended => Assert.Equal(
            ("failed", "The agent couldn't start: Agent account not found."),
            (ended.GetProperty("stopReason").GetString(), ended.GetProperty("failure").GetString())));
    }

    private async Task TheModelGatewayAnswersOnlyChatsAsync()
    {
        using HttpClient caller = new HttpClient { BaseAddress = app.ModelGatewayUrl };
        using HttpRequestMessage withoutToken = new HttpRequestMessage(HttpMethod.Post, new Uri("v1/messages", UriKind.Relative));
        using HttpRequestMessage withAnyToken = new HttpRequestMessage(HttpMethod.Post, new Uri("v1/messages", UriKind.Relative));
        withAnyToken.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-chat-token");

        using HttpResponseMessage refused = await caller.SendAsync(withoutToken, Ct);
        using HttpResponseMessage alsoRefused = await caller.SendAsync(withAnyToken, Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, alsoRefused.StatusCode);
    }
}
