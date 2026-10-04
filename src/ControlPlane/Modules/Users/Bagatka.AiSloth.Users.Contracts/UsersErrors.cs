using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// Errors callers of <see cref="IUsersApi"/> may branch on.
/// </summary>
public static class UsersErrors
{
    /// <summary>The user doesn't exist.</summary>
    public static readonly Error NotFound = Error.NotFound("users.not_found", "User not found.");
}
