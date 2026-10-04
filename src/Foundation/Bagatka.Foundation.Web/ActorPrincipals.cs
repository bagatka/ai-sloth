using System;
using System.Globalization;
using System.Security.Claims;

namespace Bagatka.Foundation.Web;

/// <summary>
/// How a host turns an authenticated request into an <see cref="Actor"/> (PATTERNS.md, entry 12).
/// </summary>
public static class ActorPrincipals
{
    private const string UserIdClaim = "bagatka_user_id";

    // Only claims this code adds count, so a token can't smuggle in a user ID of its own.
    private const string ClaimIssuer = "bagatka";

    /// <summary>Records which user the identity is, once the host resolved the token's subject.</summary>
    public static void AddUser(this ClaimsIdentity identity, UserId userId)
    {
        ArgumentNullException.ThrowIfNull(identity);
        identity.AddClaim(new Claim(UserIdClaim, userId.Value.ToString("D", CultureInfo.InvariantCulture), ClaimValueTypes.String, ClaimIssuer));
    }

    /// <summary>The actor a request runs as: the user the host recorded, or anonymous.</summary>
    public static Actor ToActor(this ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);
        Claim? user = principal.FindFirst(claim => string.Equals(claim.Type, UserIdClaim, StringComparison.Ordinal) && string.Equals(claim.Issuer, ClaimIssuer, StringComparison.Ordinal));
        return user is not null && Guid.TryParse(user.Value, CultureInfo.InvariantCulture, out Guid userId)
            ? Actor.ForUser(UserId.From(userId))
            : Actor.Anonymous;
    }
}
