using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Users.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Web;
using Bagatka.PostHog;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bagatka.AiSloth.WebApi.Endpoints;

/// <summary>
/// How people sign in to this host, from any client: with a code (the host's setup code, a link code
/// from another of their devices, or an invite where invites sign people up), or through the host's
/// identity provider in a browser, as OAuth's authorization code flow with PKCE and a loopback
/// redirect. Either way the client ends with a session token, and someone new gets a workspace of
/// their own. The browser round trip keeps no state here: what it carries is protected and expires.
/// </summary>
internal static class SignInEndpoints
{
    // Discovery's version of the public API, for clients of many hosts.
    private const int ApiVersion = 1;

    private static readonly TimeSpan BrowserLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan ClientCodeLifetime = TimeSpan.FromMinutes(2);

    // Who signs people in, to Users.
    private static readonly Actor SignInActor = Actor.ForSystem("webapi.sign-in");

    internal sealed record HostDiscovery(string Name, int ApiVersion, SignInMethods SignIn, PostHogProject? PostHog);

    internal sealed record PostHogProject(Uri Host, string ProjectToken);

    internal sealed record SignInMethods(string? Provider, bool InviteSignUp);

    internal sealed record CodeSignInRequest(string Code, string Device, string? Name = null);

    internal sealed record TokenRequest(string Code, string CodeVerifier, Uri RedirectUri);

    // The session's ID lets the device end it when it signs out (DELETE /users/me/sessions/{id}).
    internal sealed record SignedIn(string Token, SessionId Session, UserSummary User, WorkspaceId? Workspace);

    // What the browser carries to the provider and back.
    internal sealed record PendingSignIn(Uri RedirectUri, string ClientState, string CodeChallenge, string Device, string Nonce, string ProviderVerifier);

    // What the client exchanges for its session: who the provider verified, for this client only.
    internal sealed record ClientCode(VerifiedIdentity Identity, string CodeChallenge, Uri RedirectUri, string Device);

    public static void MapSignInEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/.well-known/aisloth", Discover).AllowAnonymous().WithTags("Sign-in");
        RouteGroupBuilder signIn = app.MapGroup("/sign-in").WithTags("Sign-in").AllowAnonymous().RequireRateLimiting(RateLimits.SignIn).ProducesProblem(StatusCodes.Status429TooManyRequests);
        signIn.MapPost("/code", SignInWithCode);
        signIn.MapGet("/", StartInBrowser).ExcludeFromDescription();
        signIn.MapGet("/callback", ReturnFromProvider).ExcludeFromDescription();
        signIn.MapPost("/token", ExchangeCode);
    }

    /// <summary>
    /// What this host is and how people sign in to it: with codes always, and with its identity
    /// provider when <c>signIn.provider</c> names one. <c>postHog</c> names the PostHog project the
    /// host's clients send their usage and errors to, when it has one; its token can only send.
    /// </summary>
    private static Ok<HostDiscovery> Discover([FromServices] HostSettings host, [FromServices] SignInProvider? provider, [FromServices] PostHogClientOptions? postHog)
    {
        PostHogProject? project = postHog is null ? null : new PostHogProject(postHog.Host, postHog.ProjectToken);
        return TypedResults.Ok(new HostDiscovery(host.Name, ApiVersion, new SignInMethods(provider?.Name, InviteSignUp(host, provider)), project));
    }

    /// <summary>
    /// Signs in with a code: the host's setup code, which makes you its first person; a link code from
    /// another of your devices; or, where the host lets invites sign people up, an invite. Someone new
    /// gives their name. The answer's token goes in each call's <c>Authorization: Bearer</c> header.
    /// </summary>
    private static async Task<Results<Ok<SignedIn>, ProblemHttpResult>> SignInWithCode(
        [FromBody] CodeSignInRequest request,
        [FromServices] IUsersApi users,
        [FromServices] IWorkspacesApi workspaces,
        [FromServices] HostSettings host,
        [FromServices] SignInProvider? provider,
        [FromServices] IProductEvents productEvents,
        CancellationToken ct)
    {
        SignIn withCode = new SignIn(new SignInProof(new SignInCode(request.Code, request.Name)), request.Device);
        Result<StartedSession> session = await users.SignInAsync(SignInActor, withCode, ct);
        bool mayBeInvite = session.Failed && session.Error == UsersErrors.CodeNotFound && InviteSignUp(host, provider);
        if (mayBeInvite)
        {
            session = await SignUpWithInviteAsync(request, users, workspaces, ct);
        }

        if (session.Failed)
        {
            return session.Error.ToProblem();
        }

        return await AnswerAsync(session.Output, mayBeInvite ? "invite" : "setup_code", workspaces, productEvents, ct);
    }

    /// <summary>
    /// Opens sign-in with the host's identity provider in a browser, for a client listening on a loopback
    /// <c>redirect_uri</c>: it comes back there with <c>code</c> and <c>state</c>, or <c>error</c>, and
    /// exchanges the code at <c>POST /sign-in/token</c> with its PKCE verifier.
    /// </summary>
    private static async Task<IResult> StartInBrowser(
        [FromQuery(Name = "redirect_uri")] Uri? redirectUri,
        [FromQuery] string? state,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
        [FromQuery] string? device,
        [FromQuery(Name = "login_hint")] string? loginHint,
        [FromServices] HostSettings host,
        [FromServices] SignInProvider? provider,
        [FromServices] IDataProtectionProvider protection,
        CancellationToken ct)
    {
        if (provider is null)
        {
            return TypedResults.NotFound("This host signs in with codes: sloth host add " + host.PublicUrl.AbsoluteUri.TrimEnd('/') + " --code <code>.");
        }

        bool valid = redirectUri is { IsAbsoluteUri: true, IsLoopback: true, Scheme: "http" }
            && state is { Length: > 0 and <= 512 }
            && codeChallenge is { Length: 43 }
            && string.Equals(codeChallengeMethod, "S256", StringComparison.Ordinal)
            && device is { Length: > 0 and <= 100 };
        if (!valid)
        {
            return TypedResults.BadRequest("Sign-in needs a loopback redirect_uri, a state, an S256 code_challenge, and a device name.");
        }

        string providerVerifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        string nonce = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        PendingSignIn pending = new PendingSignIn(redirectUri!, state!, codeChallenge!, device!, nonce, providerVerifier);
        string protectedState = Protect(protection, "sign-in.pending", pending, BrowserLifetime);
        Uri authorize = await provider.AuthorizationUrlAsync(Callback(host), protectedState, nonce, Challenge(providerVerifier), loginHint, ct);
        return TypedResults.Redirect(authorize.AbsoluteUri);
    }

    // The provider sends the browser back here; it goes on to the client with a code of the host's own.
    private static async Task<IResult> ReturnFromProvider(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromServices] HostSettings host,
        [FromServices] SignInProvider? provider,
        [FromServices] IDataProtectionProvider protection,
        CancellationToken ct)
    {
        PendingSignIn? pending = Unprotect<PendingSignIn>(protection, "sign-in.pending", state);
        if (provider is null || pending is null)
        {
            return TypedResults.BadRequest("This sign-in expired or isn't this host's; start again.");
        }

        if (error is not null || code is null)
        {
            return TypedResults.Redirect(ClientRedirect(pending, "error=access_denied"));
        }

        VerifiedIdentity? identity = await provider.ExchangeAsync(Callback(host), code, pending.ProviderVerifier, pending.Nonce, ct);
        if (identity is null)
        {
            return TypedResults.Redirect(ClientRedirect(pending, "error=server_error"));
        }

        ClientCode clientCode = new ClientCode(identity, pending.CodeChallenge, pending.RedirectUri, pending.Device);
        string protectedCode = Protect(protection, "sign-in.code", clientCode, ClientCodeLifetime);
        return TypedResults.Redirect(ClientRedirect(pending, "code=" + Uri.EscapeDataString(protectedCode)));
    }

    /// <summary>
    /// Exchanges the code a browser sign-in brought back for a session, with the PKCE verifier and the
    /// same redirect URI. Codes last two minutes; someone new is recorded only here.
    /// </summary>
    private static async Task<Results<Ok<SignedIn>, ProblemHttpResult>> ExchangeCode(
        [FromBody] TokenRequest request,
        [FromServices] IUsersApi users,
        [FromServices] IWorkspacesApi workspaces,
        [FromServices] IDataProtectionProvider protection,
        [FromServices] IProductEvents productEvents,
        CancellationToken ct)
    {
        ClientCode? code = Unprotect<ClientCode>(protection, "sign-in.code", request.Code);
        bool genuine = code is not null
            && code.RedirectUri == request.RedirectUri
            && string.Equals(code.CodeChallenge, Challenge(request.CodeVerifier ?? string.Empty), StringComparison.Ordinal);
        if (!genuine)
        {
            return Error.Validation("code", "The code is unknown, expired, or isn't for this verifier; sign in again.").ToProblem();
        }

        Result<StartedSession> session = await users.SignInAsync(SignInActor, new SignIn(new SignInProof(code!.Identity), code.Device), ct);
        if (session.Failed)
        {
            return session.Error.ToProblem();
        }

        return await AnswerAsync(session.Output, "provider", workspaces, productEvents, ct);
    }

    // Someone new joins with an invite: the invite must be good before they are recorded, then it is
    // theirs. Someone else may use it in between; then the code no longer works for them either.
    private static async Task<Result<StartedSession>> SignUpWithInviteAsync(CodeSignInRequest request, IUsersApi users, IWorkspacesApi workspaces, CancellationToken ct)
    {
        AcceptInvite invite = new AcceptInvite(request.Code);
        Result<Resource> checkedInvite = await workspaces.CheckInviteAsync(Actor.Anonymous, invite, ct);
        if (checkedInvite.Failed)
        {
            return new Result<StartedSession>(UsersErrors.CodeNotFound);
        }

        SignIn asNewcomer = new SignIn(new SignInProof(new Newcomer(request.Name ?? string.Empty)), request.Device);
        Result<StartedSession> session = await users.SignInAsync(SignInActor, asNewcomer, ct);
        if (session.Failed)
        {
            return session;
        }

        Result<Resource> accepted = await workspaces.AcceptInviteAsync(Actor.ForUser(session.Output.Person.Id), invite, ct);
        if (accepted.Failed)
        {
            // Not handled: the newcomer and their session, never shown, stay behind; removing people
            // isn't built.
            return new Result<StartedSession>(UsersErrors.CodeNotFound);
        }

        return session;
    }

    // The client's answer. Someone new first gets a workspace of their own, named after them, and is
    // counted as signed up, by how: with the host's setup code, an invite, or its provider.
    // Not handled: that workspace failing to be created; the person signs in again without one.
    private static async Task<Results<Ok<SignedIn>, ProblemHttpResult>> AnswerAsync(StartedSession session, string method, IWorkspacesApi workspaces, IProductEvents productEvents, CancellationToken ct)
    {
        WorkspaceId? workspace = null;
        if (session.IsNew)
        {
            Result<WorkspaceSummary> own = await workspaces.CreateAsync(Actor.ForUser(session.Person.Id), new CreateWorkspace(session.Person.Name), ct);
            if (own.Failed)
            {
                return own.Error.ToProblem();
            }

            workspace = own.Output.Id;
            productEvents.Capture(new ProductEvent("signed_up", session.Person.Id, workspace.Value.Value, new Dictionary<string, ProductFact>(StringComparer.Ordinal)
            {
                ["method"] = new ProductFact(method),
            }));
        }

        return TypedResults.Ok(new SignedIn(session.Token, session.Id, session.Person, workspace));
    }

    private static bool InviteSignUp(HostSettings host, SignInProvider? provider)
    {
        return host.InviteSignUp ?? provider is null;
    }

    private static Uri Callback(HostSettings host)
    {
        return new Uri(host.PublicUrl.AbsoluteUri.TrimEnd('/') + "/sign-in/callback");
    }

    private static string ClientRedirect(PendingSignIn pending, string result)
    {
        string separator = string.IsNullOrEmpty(pending.RedirectUri.Query) ? "?" : "&";
        return pending.RedirectUri.AbsoluteUri + separator + result + "&state=" + Uri.EscapeDataString(pending.ClientState);
    }

    private static string Challenge(string verifier)
    {
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    }

    private static string Protect<T>(IDataProtectionProvider protection, string purpose, T value, TimeSpan lifetime)
    {
        ITimeLimitedDataProtector protector = protection.CreateProtector(purpose).ToTimeLimitedDataProtector();
        return protector.Protect(JsonSerializer.Serialize(value, FoundationJson.Options), lifetime);
    }

    private static T? Unprotect<T>(IDataProtectionProvider protection, string purpose, string? text)
        where T : class
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        try
        {
            ITimeLimitedDataProtector protector = protection.CreateProtector(purpose).ToTimeLimitedDataProtector();
            return JsonSerializer.Deserialize<T>(protector.Unprotect(text), FoundationJson.Options);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
