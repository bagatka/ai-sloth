using Bagatka.AiSloth.Machines.Data;
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
    public async Task<Result<MachineSummary>> GetAsync(Actor actor, MachineId id, CancellationToken ct)
    {
        await using MachinesDbContext db = await databases.CreateDbContextAsync(ct);

        Machine? machine = await db.Machines.AsNoTracking().SingleOrDefaultAsync(found => found.Id == id, ct);
        if (machine is null)
        {
            return new Result<MachineSummary>(MachinesErrors.NotFound);
        }

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(machine.WorkspaceId), ct);
        if (access is null)
        {
            return new Result<MachineSummary>(MachinesErrors.NotFound);
        }

        return new Result<MachineSummary>(Summary(machine));
    }
}
