using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Someone started a chat that fixes a failed setup, so the chat it failed in goes on as it is: in
/// <paramref name="FixingIn"/>, with the same repositories and agent in a new nook, the agent is asked
/// to fix it and commit the fix, then the setup runs again. Both chats tell it.
/// </summary>
/// <param name="FailedIn">The chat whose nook's setup failed.</param>
/// <param name="FixingIn">The chat that fixes it.</param>
/// <param name="StartedBy">Who started it.</param>
public sealed record SetupFixStarted(ChatId FailedIn, ChatId FixingIn, UserId StartedBy);
