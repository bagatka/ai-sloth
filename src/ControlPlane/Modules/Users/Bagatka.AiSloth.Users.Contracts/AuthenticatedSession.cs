using Bagatka.Foundation;

namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>A session a call came with, and whose it is.</summary>
/// <param name="Id">The session.</param>
/// <param name="UserId">Its person.</param>
public sealed record AuthenticatedSession(SessionId Id, UserId UserId);
