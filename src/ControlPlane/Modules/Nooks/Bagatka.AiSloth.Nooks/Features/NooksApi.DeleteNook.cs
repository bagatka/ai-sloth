using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result> DeleteAsync(Actor actor, NookId id, CancellationToken ct)
    {
        Nook? nook = await FindNookAsync(actor, id, ct);
        if (nook is null)
        {
            return new Result(NooksErrors.NotFound);
        }

        nook.Delete();
        Result saved = await db.SaveAsync(ct);
        reconciler.Wake();
        return saved;
    }
}
