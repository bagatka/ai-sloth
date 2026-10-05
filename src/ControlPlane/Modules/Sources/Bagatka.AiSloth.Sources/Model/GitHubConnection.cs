using System;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.GitHub;

namespace Bagatka.AiSloth.Sources.Model;

// A person's GitHub account, connected through the host's GitHub App: the user access token that acts
// as them, kept only sealed, and renewed before it expires when GitHub gave a refresh token.
internal sealed class GitHubConnection
{
    // Renewed this long before it expires, so no git operation starts with a token about to end.
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(10);

    // Used by Connect and by EF: parameter names match property names.
    private GitHubConnection(UserId userId, long gitHubUserId, string login, string? name, DateTimeOffset connectedAt)
    {
        UserId = userId;
        GitHubUserId = gitHubUserId;
        Login = login;
        Name = name;
        ConnectedAt = connectedAt;
    }

    public UserId UserId { get; private set; }

    public long GitHubUserId { get; private set; }

    public string Login { get; private set; }

    public string? Name { get; private set; }

    public DateTimeOffset ConnectedAt { get; private set; }

    public byte[] SealedAccessToken { get; private set; } = [];

    public DateTimeOffset? AccessExpiresAt { get; private set; }

    public byte[]? SealedRefreshToken { get; private set; }

    public DateTimeOffset? RefreshExpiresAt { get; private set; }

    // PostgreSQL's xmin: a renewal and a reconnection at once conflict instead of overwriting each other.
    public uint Version { get; private set; }

    public static GitHubConnection Connect(UserId userId, GitHubUser user, GitHubUserTokens tokens, SecretBox box, TimeProvider time)
    {
        GitHubConnection connection = new GitHubConnection(userId, user.Id, user.Login, user.Name, time.GetUtcNow());
        connection.Renewed(tokens, box, time);
        return connection;
    }

    // Connecting again replaces the account and its tokens.
    public void Reconnect(GitHubUser user, GitHubUserTokens tokens, SecretBox box, TimeProvider time)
    {
        GitHubUserId = user.Id;
        Login = user.Login;
        Name = user.Name;
        ConnectedAt = time.GetUtcNow();
        Renewed(tokens, box, time);
    }

    public void Renewed(GitHubUserTokens tokens, SecretBox box, TimeProvider time)
    {
        DateTimeOffset now = time.GetUtcNow();
        SealedAccessToken = box.Seal(tokens.AccessToken, UserId.Value);
        AccessExpiresAt = now + tokens.ExpiresIn;
        SealedRefreshToken = tokens.RefreshToken is null ? null : box.Seal(tokens.RefreshToken, UserId.Value);
        RefreshExpiresAt = now + tokens.RefreshTokenExpiresIn;
    }

    public bool RenewalDue(DateTimeOffset now)
    {
        return AccessExpiresAt is DateTimeOffset expires && now >= expires - RenewalMargin;
    }

    public string AccessToken(SecretBox box)
    {
        return box.Open(SealedAccessToken, UserId.Value);
    }

    // Null when GitHub gave none, or it expired: the person connects again.
    public string? RefreshToken(SecretBox box, DateTimeOffset now)
    {
        bool usable = SealedRefreshToken is not null && (RefreshExpiresAt is null || now < RefreshExpiresAt);
        return usable ? box.Open(SealedRefreshToken!, UserId.Value) : null;
    }

    // Who commits name by default: the account, with GitHub's no-reply address, so commits link to it.
    public GitIdentity Identity()
    {
        GitHubUser user = new GitHubUser(GitHubUserId, Login, Name);
        return new GitIdentity(Name ?? Login, user.NoReplyEmail);
    }

    public GitHubAccount ToAccount()
    {
        return new GitHubAccount(Login);
    }
}
