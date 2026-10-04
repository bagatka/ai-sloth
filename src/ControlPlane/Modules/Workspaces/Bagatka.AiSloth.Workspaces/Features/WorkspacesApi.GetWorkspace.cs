using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Workspaces;

internal sealed partial class WorkspacesApi
{
    public async Task<Result<WorkspaceSummary>> GetAsync(Actor actor, WorkspaceId id, CancellationToken ct)
    {
        // Only members see a workspace; everyone else learns nothing about it.
        if (actor is not UserActor user)
        {
            return new Result<WorkspaceSummary>(WorkspacesErrors.NotFound);
        }

        WorkspaceSummary? workspace = await MembershipsOf(user.UserId)
            .Where(membership => membership.Id == id)
            .Select(membership => new WorkspaceSummary(membership.Id, membership.Name.Value, membership.Role))
            .SingleOrDefaultAsync(ct);

        return workspace is null
            ? new Result<WorkspaceSummary>(WorkspacesErrors.NotFound)
            : new Result<WorkspaceSummary>(workspace);
    }
}
