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

        Result<WorkspaceName> name = WorkspaceName.Parse(command.Name);
        if (name.Failed)
        {
            return new Result<WorkspaceSummary>(name.Error);
        }

        Workspace workspace = Workspace.Create(name.Output, user.UserId, time);
        db.Workspaces.Add(workspace);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<WorkspaceSummary>(saved.Error);
        }

        return new Result<WorkspaceSummary>(new WorkspaceSummary(workspace.Id, workspace.Name.Value, WorkspaceRole.Owner));
    }
}
