using Bagatka.AiSloth.Sources.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Sources;

internal sealed partial class SourcesApi
{
    // Not handled: revoking the person's authorization at GitHub as well, which takes the app's client
    // credentials; until they revoke it there, the app stays in their authorized apps, unused.
    public async Task<Result> DisconnectGitHubAsync(Actor actor, CancellationToken ct)
    {
        await using SourcesDbContext db = await databases.CreateDbContextAsync(ct);

        if (actor is not UserActor user)
        {
            return new Result(Error.Unauthorized);
        }

        await db.GitHubConnections.Where(connection => connection.UserId == user.UserId).ExecuteDeleteAsync(ct);
        return new Result(new Success());
    }
}
