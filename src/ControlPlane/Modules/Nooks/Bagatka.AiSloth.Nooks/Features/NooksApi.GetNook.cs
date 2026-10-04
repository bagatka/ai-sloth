using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<NookSummary>> GetAsync(Actor actor, NookId id, CancellationToken ct)
    {
        Nook? nook = await FindNookAsync(actor, id, ct);
        return nook is null ? new Result<NookSummary>(NooksErrors.NotFound) : new Result<NookSummary>(nook.ToSummary());
    }
}
