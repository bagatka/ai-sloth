namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// The setup the agent wrote is being tested in a fresh nook with only the chat's files at its latest
/// checkpoint; the chat's next turn waits for the test.
/// </summary>
/// <param name="Test">Which test this is, 1 to 3: each failed one but the last goes back to the agent to fix.</param>
public sealed record SetupTestStarted(int Test);
