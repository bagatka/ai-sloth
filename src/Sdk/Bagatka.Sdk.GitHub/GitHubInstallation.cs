namespace Bagatka.Sdk.GitHub;

/// <summary>An installation of a GitHub App on an account or organization.</summary>
/// <param name="Id">The installation.</param>
/// <param name="Account">The account or organization it is installed on.</param>
public sealed record GitHubInstallation(long Id, string Account);
