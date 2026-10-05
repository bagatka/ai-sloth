using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Secrets.Contracts;
using Bagatka.AiSloth.Secrets.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Secrets;

internal sealed partial class SecretsApi
{
    public async Task<Result<SecretSummary>> SetAsync(Actor actor, SetSecret command, CancellationToken ct)
    {
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(command.WorkspaceId), ct);
        if (access is null)
        {
            return new Result<SecretSummary>(WorkspacesErrors.NotFound);
        }

        if (actor is not UserActor user || access < AccessLevel.Manage)
        {
            return new Result<SecretSummary>(Error.Forbidden);
        }

        Result<SecretName> name = SecretName.Parse(command.Name);
        if (name.Failed)
        {
            return new Result<SecretSummary>(name.Error);
        }

        Secret? secret = await db.Secrets.SingleOrDefaultAsync(found => found.WorkspaceId == command.WorkspaceId && found.Name == name.Output, ct);
        if (secret is not null)
        {
            Result replaced = secret.Replace(command.Value, user.UserId, box, time);
            if (replaced.Failed)
            {
                return new Result<SecretSummary>(replaced.Error);
            }
        }
        else
        {
            // Not handled: two people adding secrets at the same moment past the limit; a workspace
            // may then hold a few more.
            int held = await db.Secrets.CountAsync(found => found.WorkspaceId == command.WorkspaceId, ct);
            if (held >= Secret.MaxPerWorkspace)
            {
                return new Result<SecretSummary>(SecretsErrors.TooMany);
            }

            Result<Secret> added = Secret.Add(command.WorkspaceId, name.Output, command.Value, user.UserId, box, time);
            if (added.Failed)
            {
                return new Result<SecretSummary>(added.Error);
            }

            secret = added.Output;
            db.Secrets.Add(secret);
        }

        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<SecretSummary>(saved.Error);
        }

        return new Result<SecretSummary>(secret.ToSummary());
    }
}
