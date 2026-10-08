using Bagatka.AiSloth.Workspaces.Data;
using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Workspaces;

internal sealed partial class WorkspacesApi
{
    public async Task<AccessLevel?> GetAccessAsync(Actor actor, Resource resource, CancellationToken ct)
    {
        await using WorkspacesDbContext db = await databases.CreateDbContextAsync(ct);

        ArgumentNullException.ThrowIfNull(resource);
        switch (actor)
        {
            case UserActor user:
                return await AccessOfAsync(db, user.UserId, resource, ct);
            case SystemActor or AnonymousActor:
                // Access is given to people; modules decide what their own processes may do.
                return null;
        }

        throw new InvalidOperationException("The actor has no kind.");
    }
}
