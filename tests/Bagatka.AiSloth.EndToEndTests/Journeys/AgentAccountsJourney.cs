using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: Alice pays for agents. Her workspace's API key is never shown again, her own accounts serve
/// only her, in every workspace, plans are personal and Claude's are off, a ChatGPT plan is added by
/// signing in; Codex and pi answer through the model gateway on an OpenAI key, a ChatGPT plan is renewed
/// one call at a time, and a plan whose sign-in ended says why.
/// </summary>
public sealed class AgentAccountsJourney(ControlPlane app) : IDisposable
{
    // Where ChatGPT sends the browser back; a client such as sloth listens there.
    private const string Callback = "http://127.0.0.1:1455/auth/callback";

    private readonly HttpClient _alice = app.ClientFor("alice-" + Guid.CreateVersion7());
    private readonly HttpClient _bob = app.ClientFor("bob-" + Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task People_add_agent_accounts_and_every_kind_pays_for_agents_through_the_gateway()
    {
        await AWorkspacesKeyIsNeverShownAgainAndOnlyItsMembersSeeItAsync();
        await OwnAccountsServeTheirOwnerInEveryWorkspaceAndPlansStayPersonalAsync();
        await SlothListsEachKindByANameThatCantBeConfusedAsync();
        await CodexAndPiAnswerThroughTheGatewayOnAnOpenAIKeyAsync();
        await AChatGptPlanIsAddedBySigningInAndRenewedOneCallAtATimeAsync();
    }

    public void Dispose()
    {
        _alice.Dispose();
        _bob.Dispose();
    }

    private async Task AWorkspacesKeyIsNeverShownAgainAndOnlyItsMembersSeeItAsync()
    {
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        string accounts = Paths.Workspace(workspace.Id) + "/agent-accounts";

        HttpResponseMessage added = await _alice.SendPostAsync(accounts, new { kind = "AnthropicApiKey", name = " Team key ", secret = "sk-ant-secret-value" });
        string body = await added.Content.ReadAsStringAsync(Ct);
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(added, HttpStatusCode.Created);
        Problem bobAdds = await Api.ProblemAsync(_bob.SendPostAsync(accounts, new { kind = "AnthropicApiKey", name = "Mine", secret = "sk" }), HttpStatusCode.NotFound);
        Problem withQuery = await Api.ProblemAsync(
            _alice.SendPostAsync(accounts, new { kind = "OpenAIApiKey", name = "Mine", secret = "sk", endpoint = "https://example.com/v1?key=secret" }), HttpStatusCode.BadRequest);
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(Paths.Account(account.Id), UriKind.Relative), Ct), HttpStatusCode.NoContent);
        IReadOnlyList<AgentAccountSummary> remaining = await Api.ReadAsync<IReadOnlyList<AgentAccountSummary>>(_alice.SendGetAsync(accounts), HttpStatusCode.OK);

        Assert.DoesNotContain("sk-ant-secret-value", body, StringComparison.Ordinal);
        Assert.Equal("Team key", account.Name);
        Assert.Null(account.OwnerId);
        Assert.Equal(WorkspacesErrors.NotFound.Code, bobAdds.Code);
        Assert.True(withQuery.Errors?.ContainsKey("endpoint"));
        Assert.Empty(remaining);
    }

    private async Task OwnAccountsServeTheirOwnerInEveryWorkspaceAndPlansStayPersonalAsync()
    {
        WorkspaceSummary acme = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        WorkspaceSummary personal = await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Mine" }), HttpStatusCode.Created);
        WorkspaceSummary bobs = await Api.ReadAsync<WorkspaceSummary>(_bob.SendPostAsync("/workspaces", new { name = "Bob's" }), HttpStatusCode.Created);

        AgentAccountSummary own = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync("/agent-accounts", new { kind = "CopilotPlan", name = "My Copilot", secret = "github_pat_example" }), HttpStatusCode.Created);
        IReadOnlyList<AgentAccountSummary> inAcme = await ListAsync(_alice, acme.Id);
        IReadOnlyList<AgentAccountSummary> inPersonal = await ListAsync(_alice, personal.Id);
        IReadOnlyList<AgentAccountSummary> inBobs = await ListAsync(_bob, bobs.Id);
        await Api.ProblemAsync(_bob.DeleteAsync(new Uri(Paths.Account(own.Id), UriKind.Relative), Ct), HttpStatusCode.NotFound);
        Problem sharedPlan = await Api.ProblemAsync(
            _alice.SendPostAsync(Paths.Workspace(acme.Id) + "/agent-accounts", new { kind = "CopilotPlan", name = "Shared", secret = "github_pat_example" }), HttpStatusCode.BadRequest);
        Problem claudePlan = await Api.ProblemAsync(
            _alice.SendPostAsync("/agent-accounts", new { kind = "ClaudePlan", name = "Mine", secret = "sk-ant-oat-example" }), HttpStatusCode.BadRequest);

        Assert.Contains(inAcme, found => found.Id == own.Id);
        Assert.Contains(inPersonal, found => found.Id == own.Id);
        Assert.DoesNotContain(inBobs, found => found.Id == own.Id);
        Assert.True(sharedPlan.Errors?.ContainsKey("kind"));
        Assert.True(claudePlan.Errors?.ContainsKey("kind"));
    }

    private async Task SlothListsEachKindByANameThatCantBeConfusedAsync()
    {
        await using SlothCli sloth = new SlothCli("erin-" + Guid.CreateVersion7());
        await sloth.RunAsync("host", "add", app.WebApiUrl.AbsoluteUri);

        int listed = await sloth.RunAsync("account", "add");

        Assert.Equal(0, listed);
        Assert.Matches("chatgpt-plan +ChatGPT Plus or Pro +sign in with ChatGPT +Codex, pi", sloth.Output);
        Assert.Matches("claude-plan +Claude Pro or Max +not allowed on ", sloth.Output);
        Assert.Matches("anthropic-api-key +Anthropic's API.+Claude Code, pi", sloth.Output);
    }

    private async Task CodexAndPiAnswerThroughTheGatewayOnAnOpenAIKeyAsync()
    {
        TestWorkspace workspace = await TestWorkspace.CreateAsync(app, _alice, openAI: true);

        JsonElement[] ended = await Task.WhenAll(TurnAsync(workspace, "codex", workspace.Account), TurnAsync(workspace, "pi", workspace.Account));

        Assert.All(ended, turn => Assert.Equal("end_turn", turn.GetProperty("stopReason").GetString()));
        Assert.Contains(app.Model.OpenAIAuthorizations, authorization => string.Equals(authorization, "Bearer " + FakeModel.ApiKey, StringComparison.Ordinal));
    }

    // Every token set the fake ChatGPT issues asks to be renewed at once, so each model call renews:
    // two agents working at the same time on one plan must never use a refresh token twice.
    private async Task AChatGptPlanIsAddedBySigningInAndRenewedOneCallAtATimeAsync()
    {
        TestWorkspace workspace = await TestWorkspace.CreateAsync(app, _alice, openAI: true);
        SignInStarted declinedStart = await StartSignInAsync();
        string state = QueryHelpers.ParseQuery(declinedStart.Url.Query)["state"].ToString();
        Problem declined = await Api.ProblemAsync(
            _alice.SendPostAsync(CompletePath(declinedStart), new { returnedTo = Callback + "?error=access_denied&state=" + state }), HttpStatusCode.BadRequest);
        SignInStarted started = await StartSignInAsync();
        Uri returnedTo = await FakeChatGpt.FollowAsync(started.Url);
        AgentAccountSummary plan = await Api.ReadAsync<AgentAccountSummary>(_alice.SendPostAsync(CompletePath(started), new { returnedTo }), HttpStatusCode.Created);
        Problem again = await Api.ProblemAsync(_alice.SendPostAsync(CompletePath(started), new { returnedTo }), HttpStatusCode.NotFound);
        int renewalsBefore = app.ChatGpt.Renewals;

        JsonElement[] ended = await Task.WhenAll(TurnAsync(workspace, "codex", plan.Id), TurnAsync(workspace, "codex", plan.Id));
        ChatSummary later = await workspace.StartChatAsync("codex", plan.Id);
        await using ChatWatch laterWatch = await ChatWatch.OpenAsync(workspace.Person, later);
        app.ChatGpt.Disconnect(QueryHelpers.ParseQuery(returnedTo.Query)["client_id"].ToString());
        await workspace.SendAsync(later, "say hello");
        JsonElement afterDisconnect = await laterWatch.NextAsync("turn-ended");
        IReadOnlyList<AgentAccountSummary> accounts = await ListAsync(_alice, workspace.Id);

        Assert.Equal("The sign-in was declined.", declined.Errors?.GetValueOrDefault("returnedTo")?.Single());
        Assert.Equal((AgentAccountKind.ChatGptPlan, false), (plan.Kind, plan.NeedsSignIn));
        Assert.Equal(AgentAccountsErrors.SignInNotFound.Code, again.Code);
        Assert.All(ended, turn => Assert.Equal("end_turn", turn.GetProperty("stopReason").GetString()));
        Assert.True(app.ChatGpt.Renewals > renewalsBefore);
        Assert.Equal(0, app.ChatGpt.ReusedRefreshTokens);
        Assert.Equal("The agent couldn't start: " + AgentAccountsErrors.SignInEnded.Message, afterDisconnect.GetProperty("failure").GetString());
        Assert.True(accounts.Single(found => found.Id == plan.Id).NeedsSignIn);
    }

    private async Task<SignInStarted> StartSignInAsync()
    {
        return await Api.ReadAsync<SignInStarted>(
            _alice.SendPostAsync("/agent-accounts/sign-ins", new { kind = "ChatGptPlan", name = "My ChatGPT", callback = Callback }), HttpStatusCode.OK);
    }

    private static string CompletePath(SignInStarted started)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/agent-accounts/sign-ins/{started.Id.Value}/complete");
    }

    private static async Task<IReadOnlyList<AgentAccountSummary>> ListAsync(HttpClient client, WorkspaceId workspace)
    {
        return await Api.ReadAsync<IReadOnlyList<AgentAccountSummary>>(client.SendGetAsync(Paths.Workspace(workspace) + "/agent-accounts"), HttpStatusCode.OK);
    }

    // A chat of the harness on the account, and how its first turn ended.
    private static async Task<JsonElement> TurnAsync(TestWorkspace workspace, string harness, AgentAccountId account)
    {
        ChatSummary chat = await workspace.StartChatAsync(harness, account);
        await using ChatWatch watch = await ChatWatch.OpenAsync(workspace.Person, chat);
        await workspace.SendAsync(chat, "say hello");
        return await watch.NextAsync("turn-ended");
    }
}
