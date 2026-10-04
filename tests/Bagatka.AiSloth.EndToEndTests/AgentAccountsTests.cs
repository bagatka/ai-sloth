using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Accounts at agent vendors: a workspace's, which its members run agents on, and people's own.
/// </summary>
public sealed class AgentAccountsTests(ControlPlane controlPlane) : IDisposable
{
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
        Assert.False(own.Shareable);
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
    public async Task Removing_an_account_takes_it_off_the_list_and_non_members_see_nothing()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync(_alice);
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(AccountsPath(workspace), new { kind = "AnthropicApiKey", name = "Team key", secret = "sk-ant-example" }), HttpStatusCode.Created);
        using HttpClient bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());

        Problem list = await Api.ProblemAsync(bob.SendGetAsync(AccountsPath(workspace)), HttpStatusCode.NotFound);
        Problem add = await Api.ProblemAsync(bob.SendPostAsync(AccountsPath(workspace), new { kind = "AnthropicApiKey", name = "Mine", secret = "sk" }), HttpStatusCode.NotFound);
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(PathOf(account), UriKind.Relative), TestContext.Current.CancellationToken), HttpStatusCode.NoContent);

        IReadOnlyList<AgentAccountSummary> remaining = await ListAsync(_alice, workspace);

        Assert.Equal(WorkspacesErrors.NotFound.Code, list.Code);
        Assert.Equal(WorkspacesErrors.NotFound.Code, add.Code);
        Assert.Empty(remaining);
    }

    public void Dispose()
    {
        _alice.Dispose();
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
