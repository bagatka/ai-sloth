using System.Threading.Tasks;
using System.Threading;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<NookSummary>> GetAsync(Actor actor, NookId id, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(actor, id, AccessLevel.Read, ct);
        if (nook.Failed)
        {
            return new Result<NookSummary>(nook.Error);
        }

        return new Result<NookSummary>(nook.Output.ToSummary());
    }
}
