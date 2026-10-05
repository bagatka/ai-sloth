using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Sources;

internal sealed partial class SourcesApi
{
    public async Task<Result<IReadOnlyList<RepositorySummary>>> ListRepositoriesAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct)
    {
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(workspaceId), ct);
        if (access is null)
        {
            return new Result<IReadOnlyList<RepositorySummary>>(WorkspacesErrors.NotFound);
        }

        List<Repository> repositories = await db.Repositories.AsNoTracking()
            .Where(repository => repository.WorkspaceId == workspaceId)
            .OrderBy(repository => repository.Name)
            .ToListAsync(ct);
        return new Result<IReadOnlyList<RepositorySummary>>([.. repositories.Select(repository => repository.ToSummary())]);
    }
}
