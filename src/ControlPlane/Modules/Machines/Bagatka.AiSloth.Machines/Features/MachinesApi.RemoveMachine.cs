using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Machines.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Machines;

internal sealed partial class MachinesApi
{
    public async Task<Result> RemoveAsync(Actor actor, MachineId id, CancellationToken ct)
    {
        Machine? machine = await db.Machines.SingleOrDefaultAsync(found => found.Id == id, ct);
        switch (machine is null ? null : await workspaces.GetRoleAsync(actor, machine.WorkspaceId, ct))
        {
            case null:
                return new Result(MachinesErrors.NotFound);
            case WorkspaceRole.Member:
                return new Result(Error.Forbidden);
            case WorkspaceRole.Owner:
                break;
        }

        // A removed machine's sandboxes live nowhere AiSloth can reach, so their placements go with it.
        await db.Placements.Where(placement => placement.MachineId == id).ExecuteDeleteAsync(ct);
        db.Machines.Remove(machine!);
        Result removed = await db.SaveAsync(ct);
        connections.Disconnect(id);
        return removed;
    }
}
