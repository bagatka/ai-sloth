using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// Errors callers of <see cref="IUsersApi"/> may branch on.
/// </summary>
public static class UsersErrors
{
    /// <summary>The user doesn't exist.</summary>
    public static readonly Error NotFound = Error.NotFound("users.not_found", "User not found.");

    /// <summary>The code is unknown, used, or expired: ask for a new one.</summary>
    public static readonly Error CodeNotFound = Error.NotFound("users.code_not_found", "The code is unknown, used, or expired.");

    /// <summary>The session doesn't exist, isn't the caller's, or already ended.</summary>
    public static readonly Error SessionNotFound = Error.NotFound("users.session_not_found", "Session not found.");
}
