using Bagatka.AiSloth.Sources.Data;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Sources;

internal sealed partial class SourcesApi
{
    public async Task<Result<GitSettings>> GetGitSettingsAsync(Actor actor, CancellationToken ct)
    {
        await using SourcesDbContext db = await databases.CreateDbContextAsync(ct);

        if (actor is not UserActor user)
        {
            return new Result<GitSettings>(Error.Unauthorized);
        }

        PersonGitSettings? stored = await db.GitSettings.AsNoTracking().SingleOrDefaultAsync(found => found.UserId == user.UserId, ct);
        PersonGitSettings chosen = stored ?? PersonGitSettings.Defaults(user.UserId);

        // The defaults come from the person's GitHub account, and the co-author line from the host's app.
        Result<Connected> connected = await ConnectedAsync(db, actor, ct);
        if (connected.Failed && connected.Error != SourcesErrors.GitHubNotConnected)
        {
            return new Result<GitSettings>(connected.Error);
        }

        if (connected.Failed)
        {
            return new Result<GitSettings>(chosen.ToSettings(gitHubIdentity: null, coAuthor: null));
        }

        string? coAuthor;
        try
        {
            coAuthor = await coAuthorLine.GetAsync(connected.Output.Token, ct);
        }
        catch (HttpRequestException exception) when (Revoked(exception))
        {
            return new Result<GitSettings>(chosen.ToSettings(gitHubIdentity: null, coAuthor: null));
        }

        return new Result<GitSettings>(chosen.ToSettings(connected.Output.Connection.Identity(), coAuthor));
    }
}
