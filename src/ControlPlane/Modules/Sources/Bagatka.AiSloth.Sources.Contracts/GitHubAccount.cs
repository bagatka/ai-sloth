namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>A person's GitHub account, connected to AiSloth.</summary>
/// <param name="Login">Its login, such as <c>octocat</c>.</param>
public sealed record GitHubAccount(string Login);
