using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Someone asked to run the nook's setup again, as its scripts are now: once the agent answered every
/// message sent before, it stops, the setup runs (<see cref="SetupStarted"/>, then
/// <see cref="SetupEnded"/>), and an agent starts again with what it installed, continuing the
/// conversation.
/// </summary>
/// <param name="AskedBy">Who asked.</param>
public sealed record SetupRunAsked(UserId AskedBy);
