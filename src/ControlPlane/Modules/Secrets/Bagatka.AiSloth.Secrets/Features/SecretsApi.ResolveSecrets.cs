using Bagatka.AiSloth.Secrets.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Secrets.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Secrets;

internal sealed partial class SecretsApi
{
    public async Task<Result<IReadOnlyDictionary<string, string>>> ResolveAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct)
    {
        await using SecretsDbContext db = await databases.CreateDbContextAsync(ct);

        // The module that starts processes decides who may; values never go to people from here.
        if (actor is not SystemActor)
        {
            return new Result<IReadOnlyDictionary<string, string>>(Error.Forbidden);
        }

        List<Secret> secrets = await db.Secrets.AsNoTracking().Where(secret => secret.WorkspaceId == workspaceId).ToListAsync(ct);
        Dictionary<string, string> values = secrets.ToDictionary(secret => secret.Name.Value, secret => secret.Open(box), StringComparer.Ordinal);
        return new Result<IReadOnlyDictionary<string, string>>(values);
    }
}
