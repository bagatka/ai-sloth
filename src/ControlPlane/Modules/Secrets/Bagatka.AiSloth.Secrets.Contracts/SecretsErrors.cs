using Bagatka.Foundation;

namespace Bagatka.AiSloth.Secrets.Contracts;

/// <summary>
/// Errors callers of <see cref="ISecretsApi"/> may branch on.
/// </summary>
public static class SecretsErrors
{
    /// <summary>The workspace has no secret by that name.</summary>
    public static readonly Error NotFound = Error.NotFound("secrets.not_found", "Secret not found.");

    /// <summary>The workspace already holds the most secrets it may: remove one first.</summary>
    public static readonly Error TooMany = Error.Conflict("secrets.too_many", "The workspace holds the most secrets it may; remove one first.");
}
