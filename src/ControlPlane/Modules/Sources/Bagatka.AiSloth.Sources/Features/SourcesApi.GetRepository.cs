using Bagatka.AiSloth.Sources.Data;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Sources;

internal sealed partial class SourcesApi
{
    public async Task<Result<RepositorySummary>> GetRepositoryAsync(Actor actor, RepositoryId id, CancellationToken ct)
    {
        await using SourcesDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Repository> repository = await FindRepositoryAsync(db, actor, id, AccessLevel.Read, ct);
        return repository.Failed ? new Result<RepositorySummary>(repository.Error) : new Result<RepositorySummary>(repository.Output.ToSummary());
    }
}
