using System;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: a host's first person signs in with its setup code; anyone can learn how to sign in, and
/// only the host's own sessions sign calls in; someone signs in with <c>sloth</c> in the browser, then
/// on a phone with a link code, and signs the phone out again.
/// </summary>
public sealed partial class HostJourney(ControlPlane app)
{
    private string Host => app.WebApiUrl.Authority;

    [Fact]
    public async Task The_first_person_sets_up_the_host_and_someone_signs_in_on_two_devices_and_out_again()
    {
        await AnyoneLearnsHowToSignInAsync();
        await TheFirstPersonTookTheSetupCodeWhichWorksOnceAsync();
        await OnlySessionsSignCallsInAsync();
        await SomeoneSignsInOnALaptopThenAPhoneAndSignsThePhoneOutAsync();
    }

    private async Task AnyoneLearnsHowToSignInAsync()
    {
        using HttpClient anonymous = app.ClientWithToken(token: null);

        SignInEndpointsShapes.HostDiscovery host = await Api.ReadAsync<SignInEndpointsShapes.HostDiscovery>(anonymous.SendGetAsync("/.well-known/aisloth"), HttpStatusCode.OK);

        Assert.Equal("AiSloth", host.Name);
        Assert.Equal("Fake", host.SignIn.Provider);
        Assert.True(host.SignIn.InviteSignUp);
    }

    private async Task TheFirstPersonTookTheSetupCodeWhichWorksOnceAsync()
    {
        SignInEndpointsShapes.SignedIn owner = app.Owner!;
        using HttpClient ownerClient = app.ClientWithToken(owner.Token);
        using HttpClient anonymous = app.ClientWithToken(token: null);

        UserProfile me = await Api.ReadAsync<UserProfile>(ownerClient.SendGetAsync("/users/me"), HttpStatusCode.OK);
        Page<WorkspaceSummary> workspaces = await Api.ReadAsync<Page<WorkspaceSummary>>(ownerClient.SendGetAsync("/workspaces"), HttpStatusCode.OK);
        Problem again = await Api.ProblemAsync(anonymous.SendPostAsync("/sign-in/code", new { code = app.SetupCode, name = "Mallory", device = "elsewhere" }), HttpStatusCode.NotFound);

        Assert.Equal("Owner", me.Name);
        Assert.Contains(workspaces.Items, found => found.Id == owner.Workspace);
        Assert.Equal(UsersErrors.CodeNotFound.Code, again.Code);
    }

    // A provider's token, even one the host would trust, isn't a session.
    private async Task OnlySessionsSignCallsInAsync()
    {
        await using FakeIssuer impostor = await FakeIssuer.StartAsync();
        using HttpClient anonymous = app.ClientWithToken(token: null);
        using HttpClient providerToken = app.ClientWithToken(impostor.TokenFor("alice"));
        using HttpClient madeUp = app.ClientWithToken("aisloth_not-a-session");

        await Api.ExpectAsync(anonymous.SendGetAsync("/workspaces"), HttpStatusCode.Unauthorized);
        await Api.ExpectAsync(providerToken.SendGetAsync("/users/me"), HttpStatusCode.Unauthorized);
        await Api.ExpectAsync(madeUp.SendGetAsync("/users/me"), HttpStatusCode.Unauthorized);
    }

    private async Task SomeoneSignsInOnALaptopThenAPhoneAndSignsThePhoneOutAsync()
    {
        string person = "dana-" + Guid.CreateVersion7();
        await using SlothCli laptop = new SlothCli(person);
        await using SlothCli phone = new SlothCli(person);

        int added = await laptop.RunAsync("host", "add", app.WebApiUrl.AbsoluteUri);
        string addedOutput = laptop.Output;
        await laptop.RunAsync("host", "link");
        string code = LinkCode().Match(laptop.Output).Groups["code"].Value;
        int wrong = await phone.RunAsync("host", "add", app.WebApiUrl.AbsoluteUri, "--code", "AAAABBBBCCCCDDDD");
        int linkedIn = await phone.RunAsync("host", "add", app.WebApiUrl.AbsoluteUri, "--code", code);
        string? laptopToken = await laptop.TokenForAsync(Host);
        string? phoneToken = await phone.TokenForAsync(Host);
        using HttpClient onLaptop = app.ClientWithToken(laptopToken);
        using HttpClient onPhone = app.ClientWithToken(phoneToken);
        UserProfile laptopSelf = await Api.ReadAsync<UserProfile>(onLaptop.SendGetAsync("/users/me"), HttpStatusCode.OK);
        UserProfile phoneSelf = await Api.ReadAsync<UserProfile>(onPhone.SendGetAsync("/users/me"), HttpStatusCode.OK);
        int reused = await phone.RunAsync("host", "add", app.WebApiUrl.AbsoluteUri, "--code", code);
        int signedOut = await phone.RunAsync("host", "remove", Host);
        await Api.ExpectAsync(onPhone.SendGetAsync("/users/me"), HttpStatusCode.Unauthorized);
        int laptopStill = await laptop.RunAsync("workspace", "list");

        Assert.Equal(0, added);
        Assert.Contains("Signed in to " + Host + " as ", addedOutput, StringComparison.Ordinal);
        Assert.Equal(1, wrong);
        Assert.Equal(0, linkedIn);
        Assert.Equal(laptopSelf.Id, phoneSelf.Id);
        Assert.Equal(1, reused);
        Assert.Equal(0, signedOut);
        Assert.Equal(0, laptopStill);
    }

    [GeneratedRegex("--code (?<code>[A-Z0-9]{16})", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinkCode();
}
