using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Sources;

internal sealed partial class SourcesApi
{
    public async Task<Result<GitSettings>> SetGitSettingsAsync(Actor actor, SetGitSettings command, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<GitSettings>(Error.Unauthorized);
        }

        PersonGitSettings? chosen = await db.GitSettings.SingleOrDefaultAsync(found => found.UserId == user.UserId, ct);
        if (chosen is null)
        {
            chosen = PersonGitSettings.Defaults(user.UserId);
            db.GitSettings.Add(chosen);
        }

        Result set = chosen.Set(command);
        if (set.Failed)
        {
            return new Result<GitSettings>(set.Error);
        }

        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<GitSettings>(saved.Error);
        }

        return await GetGitSettingsAsync(actor, ct);
    }
}
