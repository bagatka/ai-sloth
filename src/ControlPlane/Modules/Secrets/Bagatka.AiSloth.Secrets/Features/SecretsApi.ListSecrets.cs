using Bagatka.AiSloth.Secrets.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Secrets.Contracts;
using Bagatka.AiSloth.Secrets.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Secrets;

internal sealed partial class SecretsApi
{
    public async Task<Result<IReadOnlyList<SecretSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct)
    {
        await using SecretsDbContext db = await databases.CreateDbContextAsync(ct);

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(workspaceId), ct);
        if (access is null)
        {
            return new Result<IReadOnlyList<SecretSummary>>(WorkspacesErrors.NotFound);
        }

        List<Secret> secrets = await db.Secrets.AsNoTracking().Where(secret => secret.WorkspaceId == workspaceId).ToListAsync(ct);
        return new Result<IReadOnlyList<SecretSummary>>(secrets
            .OrderBy(secret => secret.Name.Value, System.StringComparer.Ordinal)
            .Select(secret => secret.ToSummary())
            .ToList());
    }
}
