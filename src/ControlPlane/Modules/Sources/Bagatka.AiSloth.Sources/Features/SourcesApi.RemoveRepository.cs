using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Sources;

internal sealed partial class SourcesApi
{
    public async Task<Result> RemoveRepositoryAsync(Actor actor, RepositoryId id, CancellationToken ct)
    {
        Result<Repository> repository = await FindRepositoryAsync(actor, id, AccessLevel.Manage, ct);
        if (repository.Failed)
        {
            return new Result(repository.Error);
        }

        db.Repositories.Remove(repository.Output);
        return await db.SaveAsync(ct);
    }
}
