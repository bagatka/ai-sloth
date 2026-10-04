using System.Threading.Tasks;
using System.Threading;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation.Modules;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result> DeleteAsync(Actor actor, NookId id, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(actor, id, AccessLevel.Write, ct);
        if (nook.Failed)
        {
            return new Result(nook.Error);
        }

        nook.Output.Delete();
        Result saved = await db.SaveAsync(ct);
        reconciler.Wake();
        return saved;
    }
}
