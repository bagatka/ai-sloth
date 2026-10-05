using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// Signs each call in with the session its <c>Authorization: Bearer</c> token belongs to; Users decides
/// whose it is (<see cref="IUsersApi.AuthenticateAsync"/>). The user's ID joins the principal, where
/// <see cref="ActorPrincipals.ToActor"/> finds it.
/// </summary>
internal sealed class SessionAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggers,
    UrlEncoder encoder,
    IUsersApi users) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggers, encoder)
{
    public const string SchemeName = "Session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string authorization = Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Bearer ", System.StringComparison.Ordinal))
        {
            return AuthenticateResult.NoResult();
        }

        UserId? user = await users.AuthenticateAsync(authorization["Bearer ".Length..], Context.RequestAborted);
        if (user is null)
        {
            return AuthenticateResult.Fail("The session is unknown, ended, or expired: sign in again.");
        }

        ClaimsIdentity identity = new ClaimsIdentity(SchemeName);
        identity.AddUser(user.Value);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }
}
