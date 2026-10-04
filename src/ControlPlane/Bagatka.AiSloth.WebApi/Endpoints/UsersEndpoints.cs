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
        return users;
    }

    /// <summary>The signed-in user; a user is recorded the first time they call the API.</summary>
    private static async Task<Results<Ok<UserProfile>, ProblemHttpResult>> GetMe(
        ClaimsPrincipal principal,
        [FromServices] IUsersApi api,
        CancellationToken ct)
    {
        Result<UserProfile> result = await api.GetMeAsync(principal.ToActor(), ct);
        return result.ToOk();
    }
}
