using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Data;
using Bagatka.AiSloth.Sources.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.GitHub;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Sources;

internal sealed partial class SourcesApi
{
    public async Task<Result<GitHubConnectionProgress>> CompleteGitHubConnectionAsync(Actor actor, GitHubConnectionAttemptId id, CancellationToken ct)
    {
        await using SourcesDbContext db = await databases.CreateDbContextAsync(ct);

        if (actor is not UserActor user)
        {
            return new Result<GitHubConnectionProgress>(Error.Unauthorized);
        }

        Result<GitHubAppSettings> app = App();
        if (app.Failed)
        {
            return new Result<GitHubConnectionProgress>(app.Error);
        }

        GitHubConnectionAttempt? attempt = await db.GitHubConnectionAttempts.SingleOrDefaultAsync(found => found.Id == id && found.UserId == user.UserId, ct);
        DateTimeOffset now = time.GetUtcNow();
        if (attempt is null || attempt.ExpiredAt(now))
        {
            return new Result<GitHubConnectionProgress>(SourcesErrors.ConnectionAttemptNotFound);
        }

        TimeSpan wait = attempt.WaitAt(now);
        if (wait > TimeSpan.Zero)
        {
            return new Result<GitHubConnectionProgress>(new GitHubConnectionProgress(Account: null, wait));
        }

        GitHubDevicePoll poll = await github.PollDeviceAsync(app.Output.ClientId, attempt.DeviceCode(box), ct);
        if (poll.Tokens is null)
        {
            return await NotYetAsync(db, attempt, poll, now, ct);
        }

        GitHubUser account = await github.GetAuthenticatedUserAsync(poll.Tokens.AccessToken, ct);
        GitHubConnection? connection = await db.GitHubConnections.SingleOrDefaultAsync(found => found.UserId == user.UserId, ct);
        if (connection is null)
        {
            connection = GitHubConnection.Connect(user.UserId, account, poll.Tokens, box, time);
            db.GitHubConnections.Add(connection);
        }
        else
        {
            connection.Reconnect(account, poll.Tokens, box, time);
        }

        db.GitHubConnectionAttempts.Remove(attempt);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<GitHubConnectionProgress>(saved.Error);
        }

        productEvents.Capture(new ProductEvent("github_connected", user.UserId, Workspace: null, new Dictionary<string, ProductFact>(StringComparer.Ordinal)));
        return new Result<GitHubConnectionProgress>(new GitHubConnectionProgress(connection.ToAccount(), TimeSpan.Zero));
    }

    // Waiting for the person, or over: expired, refused, or anything else GitHub says.
    private static async Task<Result<GitHubConnectionProgress>> NotYetAsync(SourcesDbContext db, GitHubConnectionAttempt attempt, GitHubDevicePoll poll, DateTimeOffset now, CancellationToken ct)
    {
        bool waiting = poll.Error is "authorization_pending" or "slow_down";
        if (waiting)
        {
            attempt.Polled(poll.Interval, now);
        }
        else
        {
            db.GitHubConnectionAttempts.Remove(attempt);
        }

        _ = await db.SaveAsync(ct);
        return waiting
            ? new Result<GitHubConnectionProgress>(new GitHubConnectionProgress(Account: null, attempt.Interval))
            : new Result<GitHubConnectionProgress>(SourcesErrors.ConnectionAttemptNotFound);
    }
}
