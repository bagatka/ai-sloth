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
        // Only a user can manage a workspace.
        if (actor is not UserActor user)
        {
            return new Result<WorkspaceSummary>(Error.Unauthorized);
        }

        Result<WorkspaceName> name = WorkspaceName.Parse(command.Name);
        if (name.Failed)
        {
            return new Result<WorkspaceSummary>(name.Error);
        }

        // The workspace and its first manager are saved together, so a workspace always has one.
        Workspace workspace = Workspace.Create(name.Output, time);
        db.Workspaces.Add(workspace);
        db.Grants.Add(Grant.Give(Resource.Workspace(workspace.Id), user.UserId, AccessLevel.Manage));
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<WorkspaceSummary>(saved.Error);
        }

        return new Result<WorkspaceSummary>(new WorkspaceSummary(workspace.Id, workspace.Name.Value, AccessLevel.Manage));
    }
}
