using System;
using System.Globalization;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.Foundation.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Bagatka.AiSloth.WebApi;

/// <summary>
/// Signs each call in with its session: the token in its <c>Authorization: Bearer</c> header, or a
/// browser's session cookie (<see cref="SessionCookie"/>). Users decides whose it is
/// (<see cref="IUsersApi.AuthenticateAsync"/>). The user's ID joins the principal, where
/// <see cref="ActorPrincipals.ToActor"/> finds it, and so does the session's, for signing out.
/// </summary>
internal sealed class SessionAuthentication(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggers,
    UrlEncoder encoder,
    IUsersApi users,
    HostSettings host) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggers, encoder)
{
    public const string SchemeName = "Session";

    public const string SessionClaim = "aisloth/session";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        string authorization = Request.Headers.Authorization.ToString();
        bool bearer = authorization.StartsWith("Bearer ", StringComparison.Ordinal);
        string? token = bearer ? authorization["Bearer ".Length..] : SessionCookie.Read(Request);
        if (token is null)
        {
            return AuthenticateResult.NoResult();
        }

        // A browser sends its cookie with every request to this host, including one another page of
        // the same site makes; a change the cookie carries must come from this host's own pages.
        bool changes = !(HttpMethods.IsGet(Request.Method) || HttpMethods.IsHead(Request.Method) || HttpMethods.IsOptions(Request.Method));
        if (!bearer && changes && !FromOwnPages(Request))
        {
            return AuthenticateResult.Fail("A session cookie changes things only from this host's own pages.");
        }

        AuthenticatedSession? session = await users.AuthenticateAsync(token, Context.RequestAborted);
        if (session is null)
        {
            return AuthenticateResult.Fail("The session is unknown, ended, or expired: sign in again.");
        }

        ClaimsIdentity identity = new ClaimsIdentity(SchemeName);
        identity.AddUser(session.UserId);
        identity.AddClaim(new Claim(SessionClaim, session.Id.Value.ToString("D", CultureInfo.InvariantCulture)));
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName));
    }

    // Browsers name the page's origin on every request that changes something.
    private bool FromOwnPages(HttpRequest request)
    {
        string origin = request.Headers.Origin.ToString();
        bool named = Uri.TryCreate(origin, UriKind.Absolute, out Uri? from);
        return named && (HostSettings.SameOrigin(from!, host.PublicUrl) || HostSettings.SameOrigin(from!, host.AppUrl));
    }
}
