namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>A repository a person's GitHub connection reaches.</summary>
/// <param name="FullName">Its name, such as <c>acme/api</c>.</param>
/// <param name="Private">Whether only people given access see it.</param>
public sealed record AvailableRepository(string FullName, bool Private);
