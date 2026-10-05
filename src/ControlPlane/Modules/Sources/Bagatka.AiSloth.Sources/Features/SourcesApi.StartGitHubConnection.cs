using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.GitHub;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Sources;

internal sealed partial class SourcesApi
{
    public async Task<Result<GitHubConnectionStarted>> StartGitHubConnectionAsync(Actor actor, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<GitHubConnectionStarted>(Error.Unauthorized);
        }

        Result<SourcesGitHubApp> app = App();
        if (app.Failed)
        {
            return new Result<GitHubConnectionStarted>(app.Error);
        }

        // Maintenance, without rules: the person's attempts that can no longer complete.
        DateTimeOffset now = time.GetUtcNow();
        await db.GitHubConnectionAttempts.Where(old => old.UserId == user.UserId && old.ExpiresAt <= now).ExecuteDeleteAsync(ct);

        GitHubDeviceCode code = await github.RequestDeviceCodeAsync(app.Output.ClientId, ct);
        GitHubConnectionAttempt attempt = GitHubConnectionAttempt.Start(user.UserId, code, box, time);
        db.GitHubConnectionAttempts.Add(attempt);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<GitHubConnectionStarted>(saved.Error);
        }

        return new Result<GitHubConnectionStarted>(attempt.ToStarted(code));
    }
}
