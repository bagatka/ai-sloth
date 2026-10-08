using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
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
/// on a phone with a link code, and signs the phone out again. The host describes its API as the
/// committed <c>openapi.json</c> does, so every change to it shows in review.
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
        await TheHostDescribesItsApiAsCommittedAsync();
        await SomeoneStartingNooksFasterThanTheHostAllowsIsAskedToWaitAsync();
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

    // Starts are bounded per person: past a burst of 20, the host answers 429 with when to try again.
    // Starts it refuses still count, so these name a provider the workspace doesn't have.
    private async Task SomeoneStartingNooksFasterThanTheHostAllowsIsAskedToWaitAsync()
    {
        using HttpClient hasty = app.ClientFor("hasty-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, hasty);
        for (int start = 0; start < 20; start++)
        {
            await Api.ExpectAsync(hasty.SendPostAsync(acme.Path + "/nooks", new { provider = "nowhere" }), HttpStatusCode.BadRequest);
        }

        using HttpResponseMessage refused = await hasty.SendPostAsync(acme.Path + "/nooks", new { provider = "nowhere" });
        Problem problem = await Api.ProblemAsync(Task.FromResult(refused), HttpStatusCode.TooManyRequests);

        Assert.Equal("host.too_many_requests", problem.Code);
        Assert.NotNull(refused.Headers.RetryAfter);
    }

    // Without its servers, which name this run's port. A changed API leaves its description beside
    // the test's output, to review and commit.
    private async Task TheHostDescribesItsApiAsCommittedAsync()
    {
        using HttpClient anonymous = app.ClientWithToken(token: null);
        using HttpResponseMessage response = await anonymous.SendGetAsync("/openapi/v1.json");
        string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        JsonObject document = JsonNode.Parse(body)!.AsObject();
        document.Remove("servers");
        string described = document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n";
        string committed = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "openapi.json"), TestContext.Current.CancellationToken);
        string changed = Path.Combine(AppContext.BaseDirectory, "TestResults", "openapi.json");
        if (!string.Equals(described, committed, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(changed)!);
            await File.WriteAllTextAsync(changed, described, TestContext.Current.CancellationToken);
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(string.Equals(described, committed, StringComparison.Ordinal), "The host's API changed; review " + changed + " and copy it to src/ControlPlane/Bagatka.AiSloth.WebApi/openapi.json.");
    }

    [GeneratedRegex("--code (?<code>[A-Z0-9]{16})", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex LinkCode();
}
