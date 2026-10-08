using Bagatka.AiSloth.Secrets.Data;
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
    public async Task<Result> RemoveAsync(Actor actor, RemoveSecret command, CancellationToken ct)
    {
        await using SecretsDbContext db = await databases.CreateDbContextAsync(ct);

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(command.WorkspaceId), ct);
        if (access is null)
        {
            return new Result(WorkspacesErrors.NotFound);
        }

        if (access < AccessLevel.Manage)
        {
            return new Result(Error.Forbidden);
        }

        Result<SecretName> name = SecretName.Parse(command.Name);
        if (name.Failed)
        {
            return new Result(SecretsErrors.NotFound);
        }

        Secret? secret = await db.Secrets.SingleOrDefaultAsync(found => found.WorkspaceId == command.WorkspaceId && found.Name == name.Output, ct);
        if (secret is null)
        {
            return new Result(SecretsErrors.NotFound);
        }

        db.Secrets.Remove(secret);
        return await db.SaveAsync(ct);
    }
}
