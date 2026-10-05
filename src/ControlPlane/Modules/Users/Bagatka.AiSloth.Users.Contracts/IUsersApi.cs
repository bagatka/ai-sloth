using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// Users: the people who use a host, by name, and their signed-in devices. People read and manage
/// themselves; the host's sign-in starts their sessions and its authentication asks whose a token is,
/// as system code, never as a route.
/// </summary>
public interface IUsersApi
{
    /// <summary>The calling user.</summary>
    /// <returns>The user; unauthorized for an actor that isn't a user; or not found for a user never recorded.</returns>
    public Task<Result<UserProfile>> GetMeAsync(Actor actor, CancellationToken ct);

    /// <summary>
    /// The names of these users, for showing who did what; IDs no user has are left out. Any signed-in
    /// person may ask.
    /// </summary>
    public Task<IReadOnlyList<UserSummary>> GetManyAsync(Actor actor, IReadOnlyCollection<UserId> ids, CancellationToken ct);

    /// <summary>
    /// A code that signs the caller in on another device (<see cref="SignInCode"/>), once, within ten
    /// minutes.
    /// </summary>
    /// <returns>The code; unauthorized for an actor that isn't a user.</returns>
    public Task<Result<LinkCode>> CreateLinkCodeAsync(Actor actor, CancellationToken ct);

    /// <summary>The caller's signed-in devices, newest first.</summary>
    /// <returns>The sessions; unauthorized for an actor that isn't a user.</returns>
    public Task<Result<IReadOnlyList<SessionSummary>>> ListSessionsAsync(Actor actor, CancellationToken ct);

    /// <summary>Signs one of the caller's devices out: its session stops working at once.</summary>
    /// <returns>Success; unauthorized for an actor that isn't a user; or not found for another person's session or one already ended.</returns>
    public Task<Result> EndSessionAsync(Actor actor, SessionId id, CancellationToken ct);

    /// <summary>
    /// Signs a person in on a device: whoever the proof names, recorded on their first sign-in, gets a
    /// new session. A provider's identity keeps the name the provider gave the first time; a code is
    /// used up only when the session starts. For system code: the host's sign-in, which validated a
    /// provider's token or checked that a newcomer may join.
    /// </summary>
    /// <returns>
    /// The session; a validation error for an invalid device or name, or an empty or oversized issuer or
    /// subject; <see cref="UsersErrors.CodeNotFound"/> for a code that is unknown, used, or expired, or a
    /// setup code once someone has signed up; or forbidden for an actor that isn't a system process.
    /// </returns>
    public Task<Result<StartedSession>> SignInAsync(Actor actor, SignIn command, CancellationToken ct);

    /// <summary>
    /// The person a session token belongs to, or <see langword="null"/> for a token that is unknown, ended,
    /// or unused for 90 days. Each use keeps the session alive. For the host's authentication.
    /// </summary>
    public Task<UserId?> AuthenticateAsync(string token, CancellationToken ct);

    /// <summary>
    /// A new setup code while nobody has signed up to the host, valid for a day and only until someone
    /// does; <see langword="null"/> once someone has. For system code at startup, which shows it to
    /// whoever runs the host.
    /// </summary>
    public Task<string?> OpenSetupAsync(Actor actor, CancellationToken ct);
}
