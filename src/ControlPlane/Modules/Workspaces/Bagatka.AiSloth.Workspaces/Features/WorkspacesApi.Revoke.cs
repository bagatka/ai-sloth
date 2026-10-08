using Bagatka.AiSloth.Workspaces.Data;
using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.AiSloth.Workspaces.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Workspaces;

internal sealed partial class WorkspacesApi
{
    public async Task<Result> RevokeAsync(Actor actor, RevokeAccess command, CancellationToken ct)
    {
        await using WorkspacesDbContext db = await databases.CreateDbContextAsync(ct);

        ArgumentNullException.ThrowIfNull(command);
        Error? refused = await RefusalAsync(actor, command.Resource, AccessLevel.Manage, ct);
        if (refused is not null)
        {
            return new Result(refused);
        }

        Grant? grant = await db.Grants.SingleOrDefaultAsync(found => found.ResourceId == command.Resource.Id && found.UserId == command.UserId, ct);
        if (grant is null)
        {
            return new Result(new Success());
        }

        // Not handled: two managers removing each other at the same moment can both pass this check
        // and leave the workspace without one; preventing it takes a serializable transaction here.
        bool lastManagerOfWorkspace = false;
        if (grant.ResourceKind == ResourceKind.Workspace && grant.Access == AccessLevel.Manage)
        {
            int managers = await db.Grants.CountAsync(found => found.ResourceId == grant.ResourceId && found.Access == AccessLevel.Manage, ct);
            lastManagerOfWorkspace = managers == 1;
        }

        if (lastManagerOfWorkspace)
        {
            return new Result(WorkspacesErrors.LastManager);
        }

        db.Grants.Remove(grant);
        return await db.SaveAsync(ct);
    }
}
