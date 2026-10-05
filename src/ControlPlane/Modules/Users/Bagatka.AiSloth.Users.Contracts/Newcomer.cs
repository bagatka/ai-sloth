namespace Bagatka.AiSloth.Users.Contracts;

/// <summary>
/// Someone new, known by name only, whom system code admits after checking they may join, such as
/// with an invite. Each sign-in with it records a new person.
/// </summary>
/// <param name="Name">Their name: 1 to 100 characters.</param>
public sealed record Newcomer(string Name);
