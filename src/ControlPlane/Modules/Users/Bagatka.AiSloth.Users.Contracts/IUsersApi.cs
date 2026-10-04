using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// Users: the people who sign in. A user is whoever an identity provider vouches for, identified by
/// the provider's issuer and its subject for that person, and recorded on their first sign-in.
/// </summary>
public interface IUsersApi
{
    /// <summary>
    /// The user an identity provider vouches for, recorded on their first sign-in. Only system code
    /// calls it, such as the WebApi's authentication after it validated the provider's token.
    /// </summary>
    /// <returns>The user; a validation error for an empty or oversized issuer or subject; or forbidden for an actor that isn't a system process.</returns>
    public Task<Result<UserId>> SignInAsync(Actor actor, VerifiedIdentity identity, CancellationToken ct);

    /// <summary>The calling user.</summary>
    /// <returns>The user; unauthorized for an actor that isn't a user; or not found for a user never recorded.</returns>
    public Task<Result<UserProfile>> GetMeAsync(Actor actor, CancellationToken ct);
}
