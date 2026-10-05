using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Accounts at agent vendors: a workspace's, which its members run agents on, and people's own.
/// </summary>
public sealed class AgentAccountsTests(ControlPlane controlPlane) : IDisposable
{
    // Where ChatGPT sends the browser back; a client such as sloth listens there.
    internal const string Callback = "http://127.0.0.1:1455/auth/callback";

    private readonly HttpClient _alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

    [Fact]
    public async Task An_owner_adds_a_workspace_account_whose_secret_is_never_shown_again()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync(_alice);

        HttpResponseMessage added = await _alice.SendPostAsync(AccountsPath(workspace), new { kind = "AnthropicApiKey", name = " Team key ", secret = "sk-ant-secret-value" });
        string body = await added.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(added, HttpStatusCode.Created);
        IReadOnlyList<AgentAccountSummary> listed = await ListAsync(_alice, workspace);

        Assert.DoesNotContain("sk-ant-secret-value", body, StringComparison.Ordinal);
        Assert.Equal("Team key", account.Name);
        Assert.Equal(workspace.Id, account.WorkspaceId);
        Assert.Null(account.OwnerId);
        Assert.Equal([account.Id], listed.Select(found => found.Id));
    }

    [Fact]
    public async Task Personal_accounts_serve_their_owner_in_every_workspace_and_nobody_else()
    {
        WorkspaceSummary acme = await CreateWorkspaceAsync(_alice);
        WorkspaceSummary personal = await CreateWorkspaceAsync(_alice);
        using HttpClient bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());
        WorkspaceSummary bobs = await CreateWorkspaceAsync(bob);

        AgentAccountSummary own = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync("/agent-accounts", new { kind = "GitHubCopilotToken", name = "My Copilot", secret = "github_pat_example" }), HttpStatusCode.Created);

        IReadOnlyList<AgentAccountSummary> inAcme = await ListAsync(_alice, acme);
        IReadOnlyList<AgentAccountSummary> inPersonal = await ListAsync(_alice, personal);
        IReadOnlyList<AgentAccountSummary> inBobs = await ListAsync(bob, bobs);

        Assert.Contains(inAcme, found => found.Id == own.Id);
        Assert.Contains(inPersonal, found => found.Id == own.Id);
        Assert.DoesNotContain(inBobs, found => found.Id == own.Id);
        await Api.ProblemAsync(bob.DeleteAsync(new Uri(PathOf(own), UriKind.Relative), TestContext.Current.CancellationToken), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Plans_are_personal_and_claude_subscriptions_are_off()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync(_alice);

        Problem copilotForEveryone = await Api.ProblemAsync(
            _alice.SendPostAsync(AccountsPath(workspace), new { kind = "GitHubCopilotToken", name = "Shared", secret = "github_pat_example" }), HttpStatusCode.BadRequest);
        Problem claudePlan = await Api.ProblemAsync(
            _alice.SendPostAsync("/agent-accounts", new { kind = "ClaudeSubscription", name = "Mine", secret = "sk-ant-oat-example" }), HttpStatusCode.BadRequest);

        Assert.True(copilotForEveryone.Errors?.ContainsKey("kind"));
        Assert.True(claudePlan.Errors?.ContainsKey("kind"));
    }

    [Fact]
    public async Task Removing_an_account_takes_it_off_the_list_and_others_see_only_their_own()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync(_alice);
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(AccountsPath(workspace), new { kind = "AnthropicApiKey", name = "Team key", secret = "sk-ant-example" }), HttpStatusCode.Created);
        using HttpClient bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());

        IReadOnlyList<AgentAccountSummary> bobSees = await ListAsync(bob, workspace);
        Problem add = await Api.ProblemAsync(bob.SendPostAsync(AccountsPath(workspace), new { kind = "AnthropicApiKey", name = "Mine", secret = "sk" }), HttpStatusCode.NotFound);
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(PathOf(account), UriKind.Relative), TestContext.Current.CancellationToken), HttpStatusCode.NoContent);

        IReadOnlyList<AgentAccountSummary> remaining = await ListAsync(_alice, workspace);

        Assert.Empty(bobSees);
        Assert.Equal(WorkspacesErrors.NotFound.Code, add.Code);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task An_api_key_may_name_another_endpoint_and_nothing_else_does()
    {
        Uri openRouter = new Uri("https://openrouter.ai/api/v1");

        AgentAccountSummary key = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync("/agent-accounts", new { kind = "OpenAIApiKey", name = "OpenRouter", secret = "sk-or-example", endpoint = openRouter }), HttpStatusCode.Created);
        Problem copilot = await Api.ProblemAsync(
            _alice.SendPostAsync("/agent-accounts", new { kind = "GitHubCopilotToken", name = "Mine", secret = "github_pat_example", endpoint = openRouter }), HttpStatusCode.BadRequest);
        Problem withQuery = await Api.ProblemAsync(
            _alice.SendPostAsync("/agent-accounts", new { kind = "OpenAIApiKey", name = "Mine", secret = "sk", endpoint = "https://example.com/v1?key=secret" }), HttpStatusCode.BadRequest);

        Assert.Equal(AgentAccountKind.OpenAIApiKey, key.Kind);
        Assert.Equal(openRouter, key.Endpoint);
        Assert.True(copilot.Errors?.ContainsKey("endpoint"));
        Assert.True(withQuery.Errors?.ContainsKey("endpoint"));
    }

    [Fact]
    public async Task A_chatgpt_plan_is_added_by_signing_in_with_chatgpt()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync(_alice);

        SignInStarted started = await StartSignInAsync(_alice);
        Uri returnedTo = await FakeChatGpt.FollowAsync(started.Url);
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(_alice.SendPostAsync(CompletePath(started), new { returnedTo }), HttpStatusCode.Created);
        IReadOnlyList<AgentAccountSummary> listed = await ListAsync(_alice, workspace);
        Problem again = await Api.ProblemAsync(_alice.SendPostAsync(CompletePath(started), new { returnedTo }), HttpStatusCode.NotFound);
        Problem withSecret = await Api.ProblemAsync(
            _alice.SendPostAsync("/agent-accounts", new { kind = "ChatGptPlan", name = "Mine", secret = "a-token" }), HttpStatusCode.BadRequest);
        Problem elsewhere = await Api.ProblemAsync(
            _alice.SendPostAsync("/agent-accounts/sign-ins", new { kind = "ChatGptPlan", name = "Mine", callback = "https://example.com/auth/callback" }), HttpStatusCode.BadRequest);

        Assert.Equal(AgentAccountKind.ChatGptPlan, account.Kind);
        Assert.Equal("My ChatGPT", account.Name);
        Assert.NotNull(account.OwnerId);
        Assert.False(account.NeedsSignIn);
        Assert.Contains(listed, found => found.Id == account.Id);
        Assert.Equal(AgentAccountsErrors.SignInNotFound.Code, again.Code);
        Assert.True(withSecret.Errors?.ContainsKey("kind"));
        Assert.True(elsewhere.Errors?.ContainsKey("callback"));
    }

    [Fact]
    public async Task A_declined_foreign_or_planless_sign_in_adds_nothing()
    {
        using HttpClient bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());
        SignInStarted started = await StartSignInAsync(_alice);
        string state = QueryHelpers.ParseQuery(started.Url.Query)["state"].ToString();
        SignInStarted planless = await StartSignInAsync(_alice);
        Uri withoutPlan = new Uri(planless.Url.AbsoluteUri.Replace("%20chatgpt.tokens.use.direct", string.Empty, StringComparison.Ordinal));

        Problem declined = await Api.ProblemAsync(
            _alice.SendPostAsync(CompletePath(started), new { returnedTo = Callback + "?error=access_denied&state=" + state }), HttpStatusCode.BadRequest);
        Problem foreign = await Api.ProblemAsync(
            _alice.SendPostAsync(CompletePath(started), new { returnedTo = Callback + "?code=stolen&client_id=oaiapp_x&state=another" }), HttpStatusCode.BadRequest);
        Uri alicesAnswer = await FakeChatGpt.FollowAsync(started.Url);
        Problem bobs = await Api.ProblemAsync(bob.SendPostAsync(CompletePath(started), new { returnedTo = alicesAnswer }), HttpStatusCode.NotFound);
        Uri planlessAnswer = await FakeChatGpt.FollowAsync(withoutPlan);
        Problem noPlan = await Api.ProblemAsync(_alice.SendPostAsync(CompletePath(planless), new { returnedTo = planlessAnswer }), HttpStatusCode.BadRequest);

        Assert.Equal("The sign-in was declined.", declined.Errors?.GetValueOrDefault("returnedTo")?.Single());
        Assert.Equal("This answer belongs to another sign-in.", foreign.Errors?.GetValueOrDefault("returnedTo")?.Single());
        Assert.Equal(AgentAccountsErrors.SignInNotFound.Code, bobs.Code);
        Assert.True(noPlan.Errors?.ContainsKey("returnedTo"));
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    internal static async Task<SignInStarted> StartSignInAsync(HttpClient client)
    {
        return await Api.ReadAsync<SignInStarted>(
            client.SendPostAsync("/agent-accounts/sign-ins", new { kind = "ChatGptPlan", name = "My ChatGPT", callback = Callback }), HttpStatusCode.OK);
    }

    internal static string CompletePath(SignInStarted started)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/agent-accounts/sign-ins/{started.Id.Value}/complete");
    }

    private static string AccountsPath(WorkspaceSummary workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/agent-accounts");
    }

    private static string PathOf(AgentAccountSummary account)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/agent-accounts/{account.Id.Value}");
    }

    private static async Task<WorkspaceSummary> CreateWorkspaceAsync(HttpClient client)
    {
        return await Api.ReadAsync<WorkspaceSummary>(client.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
    }

    private static async Task<IReadOnlyList<AgentAccountSummary>> ListAsync(HttpClient client, WorkspaceSummary workspace)
    {
        return await Api.ReadAsync<IReadOnlyList<AgentAccountSummary>>(client.SendGetAsync(AccountsPath(workspace)), HttpStatusCode.OK);
    }
}
