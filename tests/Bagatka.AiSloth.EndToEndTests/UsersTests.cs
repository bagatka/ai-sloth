using System;
using Bagatka.Foundation;
using Bagatka.AiSloth.Workspaces.Contracts;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Sign-in: with the host's setup code, link codes, invites, or its identity provider, each ending in a
/// session per device; only sessions sign calls in, and each identity is one user.
/// </summary>
public sealed class UsersTests(ControlPlane controlPlane)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task An_identity_is_recorded_once_and_stays_the_same_user()
    {
        string subject = "alice-" + Guid.CreateVersion7();
        using HttpClient firstVisit = controlPlane.ClientFor(subject);
        using HttpClient laterVisit = controlPlane.ClientFor(subject);
        using HttpClient someoneElse = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());

        UserProfile first = await Api.ReadAsync<UserProfile>(firstVisit.SendGetAsync("/users/me"), HttpStatusCode.OK);
        UserProfile later = await Api.ReadAsync<UserProfile>(laterVisit.SendGetAsync("/users/me"), HttpStatusCode.OK);
        UserProfile other = await Api.ReadAsync<UserProfile>(someoneElse.SendGetAsync("/users/me"), HttpStatusCode.OK);

        Assert.Equal(first, later);
        Assert.NotEqual(first.Id, other.Id);
    }

    [Fact]
    public async Task Calls_without_a_token_are_refused()
    {
        using HttpClient anonymous = controlPlane.ClientWithToken(token: null);

        await Api.ExpectAsync(anonymous.SendGetAsync("/workspaces"), HttpStatusCode.Unauthorized);
    }

    // Only the host's own sessions sign calls in: a provider's token, even one it would trust, isn't one.
    [Fact]
    public async Task Tokens_that_arent_sessions_are_refused()
    {
        await using FakeIssuer impostor = await FakeIssuer.StartAsync();
        using HttpClient providerToken = controlPlane.ClientWithToken(impostor.TokenFor("alice"));
        using HttpClient madeUp = controlPlane.ClientWithToken("aisloth_not-a-session");

        await Api.ExpectAsync(providerToken.SendGetAsync("/users/me"), HttpStatusCode.Unauthorized);
        await Api.ExpectAsync(madeUp.SendGetAsync("/users/me"), HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_host_says_to_anyone_how_to_sign_in()
    {
        using HttpClient anonymous = controlPlane.ClientWithToken(token: null);

        SignInEndpointsShapes.HostDiscovery host = await Api.ReadAsync<SignInEndpointsShapes.HostDiscovery>(anonymous.SendGetAsync("/.well-known/aisloth"), HttpStatusCode.OK);

        Assert.Equal("AiSloth", host.Name);
        Assert.Equal(1, host.ApiVersion);
        Assert.Equal("Fake", host.SignIn.Provider);
        Assert.True(host.SignIn.InviteSignUp);
    }

    [Fact]
    public async Task The_hosts_first_person_signed_in_with_its_setup_code_which_works_once()
    {
        SignInEndpointsShapes.SignedIn owner = controlPlane.Owner!;
        using HttpClient ownerClient = controlPlane.ClientWithToken(owner.Token);
        using HttpClient anonymous = controlPlane.ClientWithToken(token: null);

        UserProfile me = await Api.ReadAsync<UserProfile>(ownerClient.SendGetAsync("/users/me"), HttpStatusCode.OK);
        Page<WorkspaceSummary> workspaces = await Api.ReadAsync<Page<WorkspaceSummary>>(ownerClient.SendGetAsync("/workspaces"), HttpStatusCode.OK);
        Problem again = await Api.ProblemAsync(
            anonymous.SendPostAsync("/sign-in/code", new { code = controlPlane.SetupCode, name = "Mallory", device = "elsewhere" }), HttpStatusCode.NotFound);

        Assert.Equal("Owner", me.Name);
        Assert.Contains(workspaces.Items, found => found.Id == owner.Workspace && string.Equals(found.Name, "Owner", StringComparison.Ordinal));
        Assert.Equal(UsersErrors.CodeNotFound.Code, again.Code);
    }

    [Fact]
    public async Task A_link_code_signs_the_same_person_in_on_another_device_once()
    {
        using HttpClient alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());
        using HttpClient anonymous = controlPlane.ClientWithToken(token: null);
        UserProfile aliceSelf = await Api.ReadAsync<UserProfile>(alice.SendGetAsync("/users/me"), HttpStatusCode.OK);

        SignInEndpointsShapes.LinkCode link = await Api.ReadAsync<SignInEndpointsShapes.LinkCode>(alice.SendPostAsync("/users/me/link-codes", new { }), HttpStatusCode.OK);
        SignInEndpointsShapes.SignedIn phone = await Api.ReadAsync<SignInEndpointsShapes.SignedIn>(
            anonymous.SendPostAsync("/sign-in/code", new { code = link.Code, device = "phone" }), HttpStatusCode.OK);
        using HttpClient phoneClient = controlPlane.ClientWithToken(phone.Token);
        UserProfile phoneSelf = await Api.ReadAsync<UserProfile>(phoneClient.SendGetAsync("/users/me"), HttpStatusCode.OK);
        Problem again = await Api.ProblemAsync(anonymous.SendPostAsync("/sign-in/code", new { code = link.Code, device = "tablet" }), HttpStatusCode.NotFound);
        IReadOnlyList<SessionSummary> devices = await Api.ReadAsync<IReadOnlyList<SessionSummary>>(alice.SendGetAsync("/users/me/sessions"), HttpStatusCode.OK);
        SessionSummary phoneSession = devices.Single(device => string.Equals(device.Device, "phone", StringComparison.Ordinal));
        await Api.ExpectAsync(alice.DeleteAsync(new Uri(string.Create(CultureInfo.InvariantCulture, $"/users/me/sessions/{phoneSession.Id.Value}"), UriKind.Relative), Ct), HttpStatusCode.NoContent);
        HttpResponseMessage signedOut = await phoneClient.SendGetAsync("/users/me");

        Assert.Equal(aliceSelf.Id, phoneSelf.Id);
        Assert.Null(phone.Workspace);
        Assert.Equal(UsersErrors.CodeNotFound.Code, again.Code);
        Assert.Equal(["e2e", "phone"], devices.Select(device => device.Device).Order(StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.Equal(HttpStatusCode.Unauthorized, signedOut.StatusCode);
    }

    [Fact]
    public async Task An_invite_signs_someone_new_up_with_a_workspace_of_their_own()
    {
        using HttpClient alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());
        using HttpClient anonymous = controlPlane.ClientWithToken(token: null);
        WorkspaceSummary acme = await Api.ReadAsync<WorkspaceSummary>(alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        string invites = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{acme.Id.Value}/invites");
        Invite invite = await Api.ReadAsync<Invite>(alice.SendPostAsync(invites, new { access = "Write" }), HttpStatusCode.OK);

        Problem nameless = await Api.ProblemAsync(anonymous.SendPostAsync("/sign-in/code", new { code = invite.Code, name = " ", device = "laptop" }), HttpStatusCode.BadRequest);
        SignInEndpointsShapes.SignedIn bob = await Api.ReadAsync<SignInEndpointsShapes.SignedIn>(
            anonymous.SendPostAsync("/sign-in/code", new { code = invite.Code, name = "Bob", device = "laptop" }), HttpStatusCode.OK);
        using HttpClient bobClient = controlPlane.ClientWithToken(bob.Token);
        Page<WorkspaceSummary> bobs = await Api.ReadAsync<Page<WorkspaceSummary>>(bobClient.SendGetAsync("/workspaces"), HttpStatusCode.OK);
        IReadOnlyList<UserSummary> named = await Api.ReadAsync<IReadOnlyList<UserSummary>>(
            alice.SendGetAsync(string.Create(CultureInfo.InvariantCulture, $"/users?ids={bob.User.Id.Value}")), HttpStatusCode.OK);
        Problem again = await Api.ProblemAsync(anonymous.SendPostAsync("/sign-in/code", new { code = invite.Code, name = "Mallory", device = "x" }), HttpStatusCode.NotFound);

        Assert.True(nameless.Errors?.ContainsKey("name"));
        Assert.Equal("Bob", bob.User.Name);
        Assert.Contains(bobs.Items, found => found.Id == acme.Id);
        Assert.Contains(bobs.Items, found => found.Id == bob.Workspace);
        Assert.Equal([new UserSummary(bob.User.Id, "Bob")], named);
        Assert.Equal(UsersErrors.CodeNotFound.Code, again.Code);
    }

    [Fact]
    public async Task The_api_describes_itself_to_anyone()
    {
        using HttpClient anonymous = controlPlane.ClientWithToken(token: null);

        HttpResponseMessage response = await anonymous.SendGetAsync("/openapi/v1.json");

        await Api.ExpectAsync(response, HttpStatusCode.OK);
        string document = await response.Content.ReadAsStringAsync(Ct);
        Assert.Contains("/workspaces/{workspaceId}/nooks", document, StringComparison.Ordinal);
    }
}
