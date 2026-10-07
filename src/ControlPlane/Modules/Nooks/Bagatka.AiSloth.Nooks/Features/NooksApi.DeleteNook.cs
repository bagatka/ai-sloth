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
    // Deleting wins over background work that changed the nook since it was read, such as the sleeper
    // putting it to sleep or evicting it: a conflicting save reads the nook again and retries.
    public async Task<Result> DeleteAsync(Actor actor, NookId id, CancellationToken ct)
    {
        Result<Nook> nook = await FindNookAsync(actor, id, AccessLevel.Write, ct);
        if (nook.Failed)
        {
            return new Result(nook.Error);
        }

        Result saved = new Result(ModuleDbContextExtensions.ConcurrencyConflict);
        for (int attempt = 0; attempt < 3 && saved.Failed && saved.Error == ModuleDbContextExtensions.ConcurrencyConflict; attempt++)
        {
            await db.Entry(nook.Output).ReloadAsync(ct);
            nook.Output.Delete();
            saved = await db.SaveAsync(ct);
        }

        reconciler.Wake();
        return saved;
    }
}
