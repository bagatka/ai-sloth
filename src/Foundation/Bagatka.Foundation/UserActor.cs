namespace Bagatka.Foundation;

/// <summary>
/// An <see cref="Actor"/> acting on behalf of a user.
/// </summary>
/// <param name="UserId">The user.</param>
public sealed record UserActor(UserId UserId);
