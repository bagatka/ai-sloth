using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Who may do what: people invited to a workspace or to one nook, with Read, Write, or Manage.
/// </summary>
public sealed class AccessTests(ControlPlane controlPlane) : IDisposable
{
    private readonly HttpClient _alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());
    private readonly HttpClient _bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_editor_invited_to_a_workspace_works_in_all_its_nooks()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        NookSummary nook = await CreateNookAsync(_alice, workspace);
        Invite invite = await InviteAsync(PathOf(workspace), AccessLevel.Write);

        Resource joined = await AcceptAsync(_bob, invite.Code);
        WorkspaceSummary seen = await Api.ReadAsync<WorkspaceSummary>(_bob.SendGetAsync(PathOf(workspace)), HttpStatusCode.OK);
        Page<NookSummary> nooks = await Api.ReadAsync<Page<NookSummary>>(_bob.SendGetAsync(PathOf(workspace) + "/nooks"), HttpStatusCode.OK);
        NookSummary created = await CreateNookAsync(_bob, workspace);

        Assert.Equal(Resource.Workspace(workspace.Id), joined);
        Assert.Equal(AccessLevel.Write, seen.Access);
        Assert.Contains(nooks.Items, found => found.Id == nook.Id);
        Assert.Equal(workspace.Id, created.WorkspaceId);
    }

    [Fact]
    public async Task A_viewer_sees_everything_and_changes_nothing()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        NookSummary nook = await CreateNookAsync(_alice, workspace, harness: "claude-code");
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(PathOf(workspace) + "/agent-accounts", new { kind = "AnthropicApiKey", name = "Team key", secret = FakeModel.ApiKey }), HttpStatusCode.Created);
        ChatSummary chat = await Api.ReadAsync<ChatSummary>(_alice.SendPostAsync(PathOf(nook) + "/chats", new { account = account.Id }), HttpStatusCode.Created);
        Invite invite = await InviteAsync(PathOf(workspace), AccessLevel.Read);
        await AcceptAsync(_bob, invite.Code);

        WorkspaceSummary seen = await Api.ReadAsync<WorkspaceSummary>(_bob.SendGetAsync(PathOf(workspace)), HttpStatusCode.OK);
        await Api.ExpectAsync(_bob.SendGetAsync(PathOf(nook)), HttpStatusCode.OK);
        await Api.ExpectAsync(_bob.SendGetAsync(PathOf(chat)), HttpStatusCode.OK);
        await Api.ExpectAsync(_bob.SendPostAsync(PathOf(workspace) + "/nooks", new { provider = "docker" }), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(_bob.DeleteAsync(new Uri(PathOf(nook), UriKind.Relative), Ct), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(_bob.SendPostAsync(PathOf(nook) + "/processes", new { command = "true" }), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(_bob.SendPostAsync(PathOf(nook) + "/chats", new { account = account.Id }), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(_bob.SendPostAsync(PathOf(chat) + "/messages", new { text = "hi" }), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(_bob.SendPostAsync(PathOf(chat) + "/stop", new { }), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(_bob.SendPostAsync(PathOf(workspace) + "/invites", new { access = "Read" }), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(_bob.SendPostAsync(PathOf(workspace) + "/agent-accounts", new { kind = "AnthropicApiKey", name = "Mine", secret = "sk" }), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(_bob.SendPostAsync(PathOf(workspace) + "/machines", new { name = "vps" }), HttpStatusCode.Forbidden);

        Assert.Equal(AccessLevel.Read, seen.Access);
    }

    [Fact]
    public async Task A_nooks_guest_sees_only_that_nook_and_proposes_on_the_workspaces_account()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        NookSummary nook = await CreateNookAsync(_alice, workspace, harness: "claude-code");
        NookSummary other = await CreateNookAsync(_alice, workspace);
        AgentAccountSummary account = await Api.ReadAsync<AgentAccountSummary>(
            _alice.SendPostAsync(PathOf(workspace) + "/agent-accounts", new { kind = "AnthropicApiKey", name = "Team key", secret = FakeModel.ApiKey }), HttpStatusCode.Created);
        ChatSummary chat = await Api.ReadAsync<ChatSummary>(_alice.SendPostAsync(PathOf(nook) + "/chats", new { account = account.Id }), HttpStatusCode.Created);
        Invite invite = await InviteAsync(PathOf(nook), AccessLevel.Write);

        Resource joined = await AcceptAsync(_bob, invite.Code);
        await Api.ExpectAsync(_bob.SendGetAsync(PathOf(nook)), HttpStatusCode.OK);
        await Api.ExpectAsync(_bob.SendGetAsync(PathOf(other)), HttpStatusCode.NotFound);
        await Api.ExpectAsync(_bob.SendGetAsync(PathOf(workspace)), HttpStatusCode.NotFound);
        await Api.ExpectAsync(_bob.SendGetAsync(PathOf(workspace) + "/nooks"), HttpStatusCode.NotFound);
        Page<WorkspaceSummary> mine = await Api.ReadAsync<Page<WorkspaceSummary>>(_bob.SendGetAsync("/workspaces"), HttpStatusCode.OK);
        ChatMessage proposal = await Api.ReadAsync<ChatMessage>(_bob.SendPostAsync(PathOf(chat) + "/messages", new { text = "Please write hello.txt" }), HttpStatusCode.OK);

        Assert.Equal(Resource.Nook(nook.Id.Value), joined);
        Assert.DoesNotContain(mine.Items, found => found.Id == workspace.Id);
        Assert.True(proposal.IsProposal);
    }

    [Fact]
    public async Task An_invite_works_once_and_managers_decide_who_keeps_access()
    {
        WorkspaceSummary workspace = await CreateWorkspaceAsync();
        UserProfile alice = await Api.ReadAsync<UserProfile>(_alice.SendGetAsync("/users/me"), HttpStatusCode.OK);
        UserProfile bob = await Api.ReadAsync<UserProfile>(_bob.SendGetAsync("/users/me"), HttpStatusCode.OK);
        using HttpClient carol = controlPlane.ClientFor("carol-" + Guid.CreateVersion7());
        Invite invite = await InviteAsync(PathOf(workspace), AccessLevel.Write);

        await AcceptAsync(_bob, invite.Code);
        Problem again = await Api.ProblemAsync(carol.SendPostAsync("/invites/accept", new { code = invite.Code }), HttpStatusCode.NotFound);
        await Api.ExpectAsync(_bob.SendPostAsync(PathOf(workspace) + "/invites", new { access = "Write" }), HttpStatusCode.Forbidden);
        GrantSummary[] people = await Api.ReadAsync<GrantSummary[]>(_alice.SendGetAsync(PathOf(workspace) + "/access"), HttpStatusCode.OK);
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(AccessPath(workspace, bob.Id), UriKind.Relative), Ct), HttpStatusCode.NoContent);
        await Api.ExpectAsync(_bob.SendGetAsync(PathOf(workspace)), HttpStatusCode.NotFound);
        Problem lastManager = await Api.ProblemAsync(_alice.DeleteAsync(new Uri(AccessPath(workspace, alice.Id), UriKind.Relative), Ct), HttpStatusCode.Conflict);

        Assert.Equal(WorkspacesErrors.InviteNotFound.Code, again.Code);
        Assert.Contains(new GrantSummary(alice.Id, AccessLevel.Manage), people);
        Assert.Contains(new GrantSummary(bob.Id, AccessLevel.Write), people);
        Assert.Equal(WorkspacesErrors.LastManager.Code, lastManager.Code);
    }

    public void Dispose()
    {
        _alice.Dispose();
        _bob.Dispose();
    }

    private static string PathOf(WorkspaceSummary workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}");
    }

    private static string PathOf(NookSummary nook)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/nooks/{nook.Id.Value}");
    }

    private static string PathOf(ChatSummary chat)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}");
    }

    private static string AccessPath(WorkspaceSummary workspace, UserId person)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}/access/{person.Value}");
    }

    private async Task<WorkspaceSummary> CreateWorkspaceAsync()
    {
        return await Api.ReadAsync<WorkspaceSummary>(_alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
    }

    private static async Task<NookSummary> CreateNookAsync(HttpClient client, WorkspaceSummary workspace, string? harness = null)
    {
        return await Api.ReadAsync<NookSummary>(client.SendPostAsync(PathOf(workspace) + "/nooks", new { provider = "docker", harness }), HttpStatusCode.Created);
    }

    private async Task<Invite> InviteAsync(string resourcePath, AccessLevel access)
    {
        return await Api.ReadAsync<Invite>(_alice.SendPostAsync(resourcePath + "/invites", new { access }), HttpStatusCode.OK);
    }

    private static async Task<Resource> AcceptAsync(HttpClient client, string code)
    {
        return await Api.ReadAsync<Resource>(client.SendPostAsync("/invites/accept", new { code }), HttpStatusCode.OK);
    }
}
