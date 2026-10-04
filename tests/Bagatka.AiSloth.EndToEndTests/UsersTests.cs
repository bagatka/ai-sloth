using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Sign-in: the WebApi accepts tokens from the configured provider only, and each identity is one user.
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

    [Fact]
    public async Task Tokens_from_another_provider_are_refused()
    {
        await using FakeIssuer impostor = await FakeIssuer.StartAsync();
        using HttpClient client = controlPlane.ClientWithToken(impostor.TokenFor("alice"));

        await Api.ExpectAsync(client.SendGetAsync("/users/me"), HttpStatusCode.Unauthorized);
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
