using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.AiSloth.Workspaces.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Workspaces;

internal sealed partial class WorkspacesApi
{
    public async Task<Result<WorkspaceSummary>> CreateAsync(Actor actor, CreateWorkspace command, CancellationToken ct)
    {
        // Only a user can own a workspace.
        if (actor is not UserActor user)
        {
            return new Result<WorkspaceSummary>(Error.Unauthorized);
        }

        if (!WorkspaceName.Parse(command.Name).TryGetValue(out WorkspaceName? name, out Error? invalid))
        {
            return new Result<WorkspaceSummary>(invalid);
        }

        Workspace workspace = Workspace.Create(name, user.UserId, time);
        db.Workspaces.Add(workspace);
        if ((await db.SaveAsync(ct)).IsError(out Error? failed))
        {
            return new Result<WorkspaceSummary>(failed);
        }

        return new Result<WorkspaceSummary>(new WorkspaceSummary(workspace.Id, workspace.Name.Value, WorkspaceRole.Owner));
    }
}
