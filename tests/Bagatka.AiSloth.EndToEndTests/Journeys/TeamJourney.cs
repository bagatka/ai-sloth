using System;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: Alice's team shares her workspace. An invite signs Bob up as an editor, Carol joins as a
/// viewer, and Dan as a guest of one nook; each does exactly what their access allows, outsiders can't
/// even learn the workspace exists, an invite works once, and Alice decides who keeps access.
/// </summary>
public sealed class TeamJourney(ControlPlane app) : IDisposable
{
    private readonly HttpClient _alice = app.ClientFor("alice-" + Guid.CreateVersion7());
    private readonly HttpClient _carol = app.ClientFor("carol-" + Guid.CreateVersion7());
    private readonly HttpClient _dan = app.ClientFor("dan-" + Guid.CreateVersion7());
    private readonly HttpClient _erin = app.ClientFor("erin-" + Guid.CreateVersion7());
    private HttpClient? _bob;
    private TestWorkspace? _acme;
    private ChatSummary? _chat;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Bob => _bob!;

    private TestWorkspace Acme => _acme!;

    private ChatSummary Chat => _chat!;

    [Fact]
    public async Task A_team_shares_a_workspace_and_each_person_does_what_their_access_allows()
    {
        await AliceCreatesTheWorkspaceAndAChatAsync();
        await AnInviteSignsBobUpAsAnEditorWhoWorksInEveryNookAsync();
        await CarolTheViewerSeesEverythingAndChangesNothingAsync();
        await DanSeesOnlyTheNookHeIsAGuestOfAndProposesInItsChatAsync();
        await OutsidersCannotLearnTheWorkspaceExistsAsync();
        await AliceDecidesWhoKeepsAccessAsync();
    }

    public void Dispose()
    {
        _alice.Dispose();
        _bob?.Dispose();
        _carol.Dispose();
        _dan.Dispose();
        _erin.Dispose();
    }

    private async Task AliceCreatesTheWorkspaceAndAChatAsync()
    {
        HttpResponseMessage response = await _alice.SendPostAsync("/workspaces", new { name = "  Team  " });
        WorkspaceSummary created = await Api.ReadAsync<WorkspaceSummary>(response, HttpStatusCode.Created);
        _acme = await TestWorkspace.CreateAsync(app, _alice);
        _chat = await Acme.StartChatAsync();

        Assert.Equal("Team", created.Name);
        Assert.Equal(AccessLevel.Manage, created.Access);
        Assert.Equal(Paths.Workspace(created.Id), response.Headers.Location?.OriginalString);
    }

    private async Task AnInviteSignsBobUpAsAnEditorWhoWorksInEveryNookAsync()
    {
        using HttpClient anonymous = app.ClientWithToken(token: null);
        Invite invite = await InviteAsync(Acme.Path, AccessLevel.Write);

        Problem nameless = await Api.ProblemAsync(anonymous.SendPostAsync("/sign-in/code", new { code = invite.Code, name = " ", device = "laptop" }), HttpStatusCode.BadRequest);
        SignInEndpointsShapes.SignedIn bob = await Api.ReadAsync<SignInEndpointsShapes.SignedIn>(
            anonymous.SendPostAsync("/sign-in/code", new { code = invite.Code, name = "Bob", device = "laptop" }), HttpStatusCode.OK);
        _bob = app.ClientWithToken(bob.Token);
        Page<WorkspaceSummary> bobs = await Api.ReadAsync<Page<WorkspaceSummary>>(Bob.SendGetAsync("/workspaces"), HttpStatusCode.OK);
        WorkspaceSummary seen = await Api.ReadAsync<WorkspaceSummary>(Bob.SendGetAsync(Acme.Path), HttpStatusCode.OK);
        Page<NookSummary> nooks = await Api.ReadAsync<Page<NookSummary>>(Bob.SendGetAsync(Acme.Path + "/nooks"), HttpStatusCode.OK);
        NookSummary created = await Api.ReadAsync<NookSummary>(Bob.SendPostAsync(Acme.Path + "/nooks", new { provider = "docker" }), HttpStatusCode.Created);
        Problem again = await Api.ProblemAsync(anonymous.SendPostAsync("/sign-in/code", new { code = invite.Code, name = "Mallory", device = "x" }), HttpStatusCode.NotFound);

        Assert.True(nameless.Errors?.ContainsKey("name"));
        Assert.Contains(bobs.Items, found => found.Id == Acme.Id);
        Assert.Contains(bobs.Items, found => found.Id == bob.Workspace);
        Assert.Equal(AccessLevel.Write, seen.Access);
        Assert.Contains(nooks.Items, found => found.Id == Chat.NookId);
        Assert.Equal(Acme.Id, created.WorkspaceId);
        Assert.Equal(UsersErrors.CodeNotFound.Code, again.Code);
    }

    private async Task CarolTheViewerSeesEverythingAndChangesNothingAsync()
    {
        Invite invite = await InviteAsync(Acme.Path, AccessLevel.Read);
        await Api.ExpectAsync(_carol.SendPostAsync("/invites/accept", new { code = invite.Code }), HttpStatusCode.OK);

        WorkspaceSummary seen = await Api.ReadAsync<WorkspaceSummary>(_carol.SendGetAsync(Acme.Path), HttpStatusCode.OK);
        await Api.ExpectAsync(_carol.SendGetAsync(Paths.Nook(Chat.NookId)), HttpStatusCode.OK);
        await Api.ExpectAsync(_carol.SendGetAsync(Paths.Chat(Chat)), HttpStatusCode.OK);
        await Api.ExpectAsync(_carol.SendPostAsync(Acme.Path + "/nooks", new { provider = "docker" }), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(_carol.SendPostAsync(Paths.Nook(Chat.NookId) + "/processes", new { command = "true" }), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(_carol.SendPostAsync(Paths.Chat(Chat) + "/messages", new { text = "hi" }), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(_carol.SendPostAsync(Acme.Path + "/invites", new { access = "Read" }), HttpStatusCode.Forbidden);
        await Api.ExpectAsync(_carol.SendPostAsync(Acme.Path + "/agent-accounts", new { kind = "AnthropicApiKey", name = "Mine", secret = "sk" }), HttpStatusCode.Forbidden);

        Assert.Equal(AccessLevel.Read, seen.Access);
    }

    private async Task DanSeesOnlyTheNookHeIsAGuestOfAndProposesInItsChatAsync()
    {
        NookSummary other = await Api.ReadAsync<NookSummary>(_alice.SendPostAsync(Acme.Path + "/nooks", new { provider = "docker" }), HttpStatusCode.Created);
        Invite invite = await InviteAsync(Paths.Nook(Chat.NookId), AccessLevel.Write);

        Resource joined = await Api.ReadAsync<Resource>(_dan.SendPostAsync("/invites/accept", new { code = invite.Code }), HttpStatusCode.OK);
        await Api.ExpectAsync(_dan.SendGetAsync(Paths.Nook(Chat.NookId)), HttpStatusCode.OK);
        await Api.ExpectAsync(_dan.SendGetAsync(Paths.Nook(other.Id)), HttpStatusCode.NotFound);
        await Api.ExpectAsync(_dan.SendGetAsync(Acme.Path), HttpStatusCode.NotFound);
        ChatMessage proposal = await Api.ReadAsync<ChatMessage>(_dan.SendPostAsync(Paths.Chat(Chat) + "/messages", new { text = "Please write hello.txt" }), HttpStatusCode.OK);

        Assert.Equal(Resource.Nook(Chat.NookId.Value), joined);
        Assert.True(proposal.IsProposal);
    }

    private async Task OutsidersCannotLearnTheWorkspaceExistsAsync()
    {
        Problem workspace = await Api.ProblemAsync(_erin.SendGetAsync(Acme.Path), HttpStatusCode.NotFound);
        Page<WorkspaceSummary> erins = await Api.ReadAsync<Page<WorkspaceSummary>>(_erin.SendGetAsync("/workspaces"), HttpStatusCode.OK);
        Problem chat = await Api.ProblemAsync(_erin.SendGetAsync(Paths.Chat(Chat)), HttpStatusCode.NotFound);
        Problem watch = await Api.ProblemAsync(_erin.SendGetAsync(Paths.Chat(Chat) + "/events"), HttpStatusCode.NotFound);
        Problem nook = await Api.ProblemAsync(_erin.SendPostAsync(Paths.Nook(Chat.NookId) + "/processes", new { command = "true" }), HttpStatusCode.NotFound);
        Problem machines = await Api.ProblemAsync(_erin.SendGetAsync(Acme.Path + "/machines"), HttpStatusCode.NotFound);

        Assert.Equal(WorkspacesErrors.NotFound.Code, workspace.Code);
        Assert.DoesNotContain(erins.Items, found => found.Id == Acme.Id);
        Assert.Equal(ChatsErrors.NotFound.Code, chat.Code);
        Assert.Equal(ChatsErrors.NotFound.Code, watch.Code);
        Assert.Equal(NooksErrors.NotFound.Code, nook.Code);
        Assert.Equal(WorkspacesErrors.NotFound.Code, machines.Code);
    }

    private async Task AliceDecidesWhoKeepsAccessAsync()
    {
        UserProfile alice = await Api.ReadAsync<UserProfile>(_alice.SendGetAsync("/users/me"), HttpStatusCode.OK);
        UserProfile bob = await Api.ReadAsync<UserProfile>(Bob.SendGetAsync("/users/me"), HttpStatusCode.OK);

        await Api.ExpectAsync(Bob.SendPostAsync(Acme.Path + "/invites", new { access = "Write" }), HttpStatusCode.Forbidden);
        GrantSummary[] people = await Api.ReadAsync<GrantSummary[]>(_alice.SendGetAsync(Acme.Path + "/access"), HttpStatusCode.OK);
        await Api.ExpectAsync(_alice.DeleteAsync(new Uri(AccessPath(bob.Id), UriKind.Relative), Ct), HttpStatusCode.NoContent);
        await Api.ExpectAsync(Bob.SendGetAsync(Acme.Path), HttpStatusCode.NotFound);
        Problem lastManager = await Api.ProblemAsync(_alice.DeleteAsync(new Uri(AccessPath(alice.Id), UriKind.Relative), Ct), HttpStatusCode.Conflict);

        Assert.Contains(new GrantSummary(alice.Id, AccessLevel.Manage), people);
        Assert.Contains(new GrantSummary(bob.Id, AccessLevel.Write), people);
        Assert.Equal(WorkspacesErrors.LastManager.Code, lastManager.Code);
    }

    private async Task<Invite> InviteAsync(string resourcePath, AccessLevel access)
    {
        return await Api.ReadAsync<Invite>(_alice.SendPostAsync(resourcePath + "/invites", new { access }), HttpStatusCode.OK);
    }

    private string AccessPath(UserId person)
    {
        return Acme.Path + string.Create(CultureInfo.InvariantCulture, $"/access/{person.Value}");
    }
}
