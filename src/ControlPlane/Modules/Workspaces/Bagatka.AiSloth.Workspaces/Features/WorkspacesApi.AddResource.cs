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
    private static readonly Error InAnotherParent = Error.Conflict("workspaces.resource_in_another", "The resource is already in another.");

    public async Task<Result> AddResourceAsync(Actor actor, AddResource command, CancellationToken ct)
    {
        await using WorkspacesDbContext db = await databases.CreateDbContextAsync(ct);

        ArgumentNullException.ThrowIfNull(command);

        // Today a nook is in its workspace; projects will add more ways to nest.
        bool nookInWorkspace = command.Resource.Kind == ResourceKind.Nook && command.Parent.Kind == ResourceKind.Workspace;
        if (!nookInWorkspace)
        {
            return new Result(Error.Validation("resource", "Only a nook can be added, to a workspace."));
        }

        Error? refused = await RefusalAsync(actor, command.Parent, AccessLevel.Write, ct);
        if (refused is not null)
        {
            return new Result(refused);
        }

        ResourceLink? existing = await db.Links.AsNoTracking().SingleOrDefaultAsync(link => link.ChildId == command.Resource.Id, ct);
        if (existing is not null)
        {
            bool sameParent = existing.ParentId == command.Parent.Id;
            return sameParent ? new Result(new Success()) : new Result(InAnotherParent);
        }

        db.Links.Add(ResourceLink.Link(command.Resource, command.Parent));
        return await db.SaveAsync(ct);
    }
}
