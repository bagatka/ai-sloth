using System;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// A user as they see themselves.
/// </summary>
/// <param name="Id">The user.</param>
/// <param name="Name">Their name.</param>
/// <param name="SignedUpAt">When they first signed in.</param>
public sealed record UserProfile(UserId Id, string Name, DateTimeOffset SignedUpAt);
