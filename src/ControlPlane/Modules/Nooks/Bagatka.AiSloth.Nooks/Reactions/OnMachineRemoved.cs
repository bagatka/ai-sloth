using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Foundation.Modules.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Nooks.Reactions;

// Nooks on a removed machine can't run anywhere any more: they fail for good, and their daemons, which
// run on that computer until its machine mode removes them, are cut off and refused, so nothing of the
// workspace, such as its secrets, reaches it again. Nooks being deleted only lose their record.
internal sealed class OnMachineRemoved(IDbContextFactory<NooksDbContext> databases, DaemonConnections daemons, ILogger<OnMachineRemoved> logger) : IReaction<MachineRemoved>
{
    public async Task<Result> HandleAsync(MachineRemoved integrationEvent, CancellationToken ct)
    {
        string location = MachineProvider.LocationOf(integrationEvent.MachineId);
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        List<Nook> stranded = await db.Nooks
            .Where(nook => nook.Provider == MachineProvider.Name && nook.Location == location && nook.Status != NookStatus.Deleting)
            .ToListAsync(ct);
        foreach (Nook nook in stranded)
        {
            nook.FailForGood();
        }

        // A conflict with a job changing one of them has the event delivered again.
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return saved;
        }

        foreach (Nook nook in stranded)
        {
            daemons.Drop(nook.Id);
            Log.NookFailed(logger, nook.Id.Value, "its machine was removed");
        }

        return saved;
    }
}
