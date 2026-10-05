using System.Globalization;

namespace Bagatka.Sdk.GitHub;

/// <summary>A GitHub account.</summary>
/// <param name="Id">Its numeric ID, which never changes.</param>
/// <param name="Login">Its login, such as <c>octocat</c>.</param>
/// <param name="Name">Its display name, if set.</param>
public sealed record GitHubUser(long Id, string Login, string? Name)
{
    /// <summary>The email address GitHub attributes commits to this account by without revealing a real one.</summary>
    public string NoReplyEmail => Id.ToString(CultureInfo.InvariantCulture) + "+" + Login + "@users.noreply.github.com";
}
