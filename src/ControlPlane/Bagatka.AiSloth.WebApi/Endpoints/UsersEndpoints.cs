using System.Linq;
using System.Collections.Generic;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bagatka.AiSloth.WebApi.Endpoints;

internal static class UsersEndpoints
{
    public static RouteGroupBuilder MapUsersEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder users = app.MapGroup("/users").WithTags("Users");
        users.MapGet("/me", GetMe);
        users.MapGet("/", GetMany);
        users.MapPost("/me/link-codes", CreateLinkCode);
        users.MapGet("/me/sessions", ListSessions);
        users.MapDelete("/me/sessions/{id:guid}", EndSession);
        return users;
    }

    /// <summary>The signed-in user.</summary>
    private static async Task<Results<Ok<UserProfile>, ProblemHttpResult>> GetMe(
        ClaimsPrincipal principal,
        [FromServices] IUsersApi api,
        CancellationToken ct)
    {
        Result<UserProfile> result = await api.GetMeAsync(principal.ToActor(), ct);
        return result.ToOk();
    }

    /// <summary>The names of these people, for showing who did what, such as a chat's messages: <c>?ids=…&amp;ids=…</c>. Unknown IDs are left out.</summary>
    private static async Task<Ok<IReadOnlyList<UserSummary>>> GetMany(
        [FromQuery] Guid[] ids,
        ClaimsPrincipal principal,
        [FromServices] IUsersApi api,
        CancellationToken ct)
    {
        IReadOnlyList<UserSummary> people = await api.GetManyAsync(principal.ToActor(), [.. ids.Select(UserId.From)], ct);
        return TypedResults.Ok(people);
    }

    /// <summary>
    /// A code that signs you in on another device, once, within ten minutes:
    /// <c>sloth host add &lt;url&gt; --code &lt;code&gt;</c> there.
    /// </summary>
    private static async Task<Results<Ok<LinkCode>, ProblemHttpResult>> CreateLinkCode(
        ClaimsPrincipal principal,
        [FromServices] IUsersApi api,
        CancellationToken ct)
    {
        Result<LinkCode> result = await api.CreateLinkCodeAsync(principal.ToActor(), ct);
        return result.ToOk();
    }

    /// <summary>Your signed-in devices, newest first.</summary>
    private static async Task<Results<Ok<IReadOnlyList<SessionSummary>>, ProblemHttpResult>> ListSessions(
        ClaimsPrincipal principal,
        [FromServices] IUsersApi api,
        CancellationToken ct)
    {
        Result<IReadOnlyList<SessionSummary>> result = await api.ListSessionsAsync(principal.ToActor(), ct);
        return result.ToOk();
    }

    /// <summary>Signs one of your devices out; its session stops working at once.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> EndSession(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IUsersApi api,
        CancellationToken ct)
    {
        Result result = await api.EndSessionAsync(principal.ToActor(), SessionId.From(id), ct);
        return result.ToNoContent();
    }
}
