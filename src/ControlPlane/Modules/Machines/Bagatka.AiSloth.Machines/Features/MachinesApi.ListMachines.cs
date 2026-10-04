using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Machines.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Machines;

internal sealed partial class MachinesApi
{
    public async Task<Result<IReadOnlyList<MachineSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct)
    {
        WorkspaceRole? role = await workspaces.GetRoleAsync(actor, workspaceId, ct);
        if (role is null)
        {
            return new Result<IReadOnlyList<MachineSummary>>(WorkspacesErrors.NotFound);
        }

        List<Machine> machines = await db.Machines.AsNoTracking()
            .Where(machine => machine.WorkspaceId == workspaceId)
            .OrderBy(machine => machine.Id)
            .ToListAsync(ct);
        return new Result<IReadOnlyList<MachineSummary>>(machines.Select(Summary).ToList());
    }
}
