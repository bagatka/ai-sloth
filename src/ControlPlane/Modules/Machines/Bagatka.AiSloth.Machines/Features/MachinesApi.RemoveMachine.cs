using Bagatka.AiSloth.Machines.Data;
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
        await using MachinesDbContext db = await databases.CreateDbContextAsync(ct);

        Machine? machine = await db.Machines.SingleOrDefaultAsync(found => found.Id == id, ct);
        if (machine is null)
        {
            return new Result(MachinesErrors.NotFound);
        }

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(machine.WorkspaceId), ct);
        if (access is null)
        {
            return new Result(MachinesErrors.NotFound);
        }

        if (access < AccessLevel.Manage)
        {
            return new Result(Error.Forbidden);
        }

        // A removed machine's sandboxes live nowhere AiSloth can reach, so their placements go with it.
        await db.Placements.Where(placement => placement.MachineId == id).ExecuteDeleteAsync(ct);
        machine.Remove(db.Outbox);
        db.Machines.Remove(machine);
        Result removed = await db.SaveAsync(ct);
        connections.Disconnect(id);
        return removed;
    }
}
