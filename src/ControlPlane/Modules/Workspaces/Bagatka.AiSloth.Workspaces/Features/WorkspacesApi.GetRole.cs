using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Workspaces;

internal sealed partial class WorkspacesApi
{
    public async Task<WorkspaceRole?> GetRoleAsync(Actor actor, WorkspaceId id, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return null;
        }

        return await db.Members
            .Where(member => member.WorkspaceId == id && member.UserId == user.UserId)
            .Select(member => (WorkspaceRole?)member.Role)
            .SingleOrDefaultAsync(ct);
    }
}
