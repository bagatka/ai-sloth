using System.Security.Claims;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// Turns a validated token into a user: Users records the token's issuer and subject on first sign-in,
/// and the user's ID joins the principal, where <see cref="ActorPrincipals.ToActor"/> finds it.
/// </summary>
internal static class TokenSignIn
{
    public static async Task RecordUserAsync(TokenValidatedContext context)
    {
        ClaimsIdentity? identity = context.Principal?.Identity as ClaimsIdentity;
        string? subject = identity?.FindFirst("sub")?.Value;
        if (identity is null || subject is null)
        {
            context.Fail("The token names no subject.");
            return;
        }

        // A framework callback can't take constructor dependencies, so it asks the request's services.
        IUsersApi users = context.HttpContext.RequestServices.GetRequiredService<IUsersApi>();
        VerifiedIdentity verified = new VerifiedIdentity(context.SecurityToken.Issuer, subject);
        Result<UserId> signedIn = await users.SignInAsync(Actor.ForSystem("webapi.authentication"), verified, context.HttpContext.RequestAborted);
        if (signedIn.Failed)
        {
            context.Fail(signedIn.Error.Message);
            return;
        }

        identity.AddUser(signedIn.Output);
    }
}
