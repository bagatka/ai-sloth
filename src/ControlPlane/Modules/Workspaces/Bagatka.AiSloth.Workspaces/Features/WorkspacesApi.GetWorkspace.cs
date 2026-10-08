using Bagatka.AiSloth.Workspaces.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.AiSloth.Workspaces.Model;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Workspaces;

internal sealed partial class WorkspacesApi
{
    public async Task<Result<WorkspaceSummary>> GetAsync(Actor actor, WorkspaceId id, CancellationToken ct)
    {
        await using WorkspacesDbContext db = await databases.CreateDbContextAsync(ct);

        // Only people with access see a workspace; everyone else learns nothing about it.
        AccessLevel? access = await GetAccessAsync(actor, Resource.Workspace(id), ct);
        if (access is null)
        {
            return new Result<WorkspaceSummary>(WorkspacesErrors.NotFound);
        }

        Workspace? workspace = await db.Workspaces.AsNoTracking().SingleOrDefaultAsync(found => found.Id == id, ct);
        if (workspace is null)
        {
            return new Result<WorkspaceSummary>(WorkspacesErrors.NotFound);
        }

        return new Result<WorkspaceSummary>(new WorkspaceSummary(workspace.Id, workspace.Name.Value, access.Value));
    }
}
