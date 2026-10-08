using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Data;
using Bagatka.AiSloth.Sources.Git;
using Bagatka.AiSloth.Sources.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.GitHub;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Sources;

// The front door for the contract, and what several features share: the host's GitHub App, a
// person's working GitHub token, and finding a repository the actor may use. Each feature is a file
// in Features/.
internal sealed partial class SourcesApi(
    IDbContextFactory<SourcesDbContext> databases,
    IWorkspacesApi workspaces,
    GitHubClient github,
    CoAuthorLine coAuthorLine,
    GitScratch scratches,
    [FromKeyedServices(SourcesDbContext.Schema)] SecretBox box,
    SourcesSettings settings,
    TimeProvider time,
    ILogger<SourcesApi> logger) : ISourcesApi
{
    // The host's GitHub App, if it has one.
    private Result<GitHubAppSettings> App()
    {
        return settings.GitHubApp is null ? new Result<GitHubAppSettings>(SourcesErrors.GitHubNotConfigured) : new Result<GitHubAppSettings>(settings.GitHubApp);
    }

    // The actor's GitHub connection with a token that works for a while yet, renewed first when due.
    private async Task<Result<Connected>> ConnectedAsync(SourcesDbContext db, Actor actor, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<Connected>(Error.Unauthorized);
        }

        GitHubConnection? connection = await db.GitHubConnections.AsNoTracking().SingleOrDefaultAsync(found => found.UserId == user.UserId, ct);
        if (connection is null)
        {
            return new Result<Connected>(SourcesErrors.GitHubNotConnected);
        }

        if (connection.RenewalDue(time.GetUtcNow()))
        {
            Result<GitHubConnection> renewed = await RenewAsync(db, user.UserId);
            if (renewed.Failed)
            {
                return new Result<Connected>(renewed.Error);
            }

            connection = renewed.Output;
        }

        return new Result<Connected>(new Connected(connection, connection.AccessToken(box)));
    }

    // Renews a person's token, one renewal at a time: GitHub replaces the refresh token with every
    // renewal and refuses one used twice, so the row stays locked while GitHub answers, and whoever
    // waited finds it renewed. The caller's cancellation doesn't apply: a renewal GitHub completed but
    // this side dropped would end the connection.
    private async Task<Result<GitHubConnection>> RenewAsync(SourcesDbContext db, UserId userId)
    {
        Result<GitHubAppSettings> app = App();
        if (app.Failed)
        {
            return new Result<GitHubConnection>(app.Error);
        }

        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(CancellationToken.None);
        GitHubConnection? connection = await db.GitHubConnections
            .FromSql($"SELECT * FROM sources.github_connections WHERE user_id = {userId.Value} FOR UPDATE")
            .SingleOrDefaultAsync(CancellationToken.None);
        DateTimeOffset now = time.GetUtcNow();
        if (connection is null)
        {
            return new Result<GitHubConnection>(SourcesErrors.GitHubNotConnected);
        }

        if (!connection.RenewalDue(now))
        {
            return new Result<GitHubConnection>(connection);
        }

        string? refreshToken = connection.RefreshToken(box, now);
        Result<GitHubUserTokens> tokens = new Result<GitHubUserTokens>(Error.Conflict("github.no_refresh_token", "GitHub gave no refresh token that still works."));
        if (refreshToken is not null)
        {
            tokens = await github.RefreshAsync(app.Output.ClientId, app.Output.ClientSecret, refreshToken, CancellationToken.None);
        }
        if (tokens.Failed)
        {
            // Every refusal means the connection is over, such as after the person revoked the app.
            Log.ConnectionEnded(logger, userId.Value, tokens.Error.Code);
            db.GitHubConnections.Remove(connection);
        }
        else
        {
            connection.Renewed(tokens.Output, box, time);
        }

        Result saved = await db.SaveAsync(CancellationToken.None);
        if (saved.Failed)
        {
            return new Result<GitHubConnection>(saved.Error);
        }

        await transaction.CommitAsync(CancellationToken.None);
        return tokens.Failed ? new Result<GitHubConnection>(SourcesErrors.GitHubNotConnected) : new Result<GitHubConnection>(connection);
    }

    // The repository, if the actor may do at least `needed` in its workspace; the control plane's own
    // processes may do anything. Not found when they may not see it.
    private async Task<Result<Repository>> FindRepositoryAsync(SourcesDbContext db, Actor actor, RepositoryId id, AccessLevel needed, CancellationToken ct)
    {
        Repository? repository = await db.Repositories.SingleOrDefaultAsync(found => found.Id == id, ct);
        if (repository is null || actor is AnonymousActor)
        {
            return new Result<Repository>(SourcesErrors.RepositoryNotFound);
        }

        if (actor is SystemActor)
        {
            return new Result<Repository>(repository);
        }

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(repository.WorkspaceId), ct);
        if (access is null)
        {
            return new Result<Repository>(SourcesErrors.RepositoryNotFound);
        }

        return access < needed ? new Result<Repository>(Error.Forbidden) : new Result<Repository>(repository);
    }

    // GitHub answering 401 means the person revoked the app or its token: they connect again.
    private static bool Revoked(HttpRequestException exception)
    {
        return exception.StatusCode == HttpStatusCode.Unauthorized;
    }

    // A person's connection and its working token. Never log it.
    private sealed record Connected(GitHubConnection Connection, string Token)
    {
        public override string ToString()
        {
            return "Connected { Login = " + Connection.Login + ", Token = *** }";
        }
    }
}
