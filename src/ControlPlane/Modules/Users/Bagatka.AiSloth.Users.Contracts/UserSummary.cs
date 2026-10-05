using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>A user as others see them.</summary>
/// <param name="Id">The user.</param>
/// <param name="Name">Their name.</param>
public sealed record UserSummary(UserId Id, string Name);
