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
        if (context.Principal?.Identity is not ClaimsIdentity identity || identity.FindFirst("sub")?.Value is not string subject)
        {
            context.Fail("The token names no subject.");
            return;
        }

        // A framework callback can't take constructor dependencies, so it asks the request's services.
        IUsersApi users = context.HttpContext.RequestServices.GetRequiredService<IUsersApi>();
        SignIn identityProviderSays = new SignIn(context.SecurityToken.Issuer, subject);
        Result<UserId> signedIn = await users.SignInAsync(Actor.ForSystem("webapi.authentication"), identityProviderSays, context.HttpContext.RequestAborted);
        if (!signedIn.TryGetValue(out UserId userId, out Error? error))
        {
            context.Fail(error.Message);
            return;
        }

        identity.AddUser(userId);
    }
}
