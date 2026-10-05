using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Sources;

internal sealed partial class SourcesApi
{
    public async Task<Result<GitHubAccount>> GetGitHubAccountAsync(Actor actor, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<GitHubAccount>(Error.Unauthorized);
        }

        GitHubConnection? connection = await db.GitHubConnections.AsNoTracking().SingleOrDefaultAsync(found => found.UserId == user.UserId, ct);
        return connection is null ? new Result<GitHubAccount>(SourcesErrors.GitHubNotConnected) : new Result<GitHubAccount>(connection.ToAccount());
    }
}
