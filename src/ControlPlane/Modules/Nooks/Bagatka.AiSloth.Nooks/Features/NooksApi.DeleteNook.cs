using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result> DeleteAsync(Actor actor, NookId id, CancellationToken ct)
    {
        await using NooksDbContext db = await databases.CreateDbContextAsync(ct);

        Result<Nook> nook = await FindNookAsync(db, actor, id, AccessLevel.Write, ct);
        if (nook.Failed)
        {
            return new Result(nook.Error);
        }

        // Deleting can't be undone, so people invited to the nook alone may not.
        if (actor is UserActor)
        {
            bool member = await WritesInWorkspaceAsync(actor, nook.Output, ct);
            if (!member)
            {
                return new Result(Error.Forbidden);
            }
        }

        // Not handled: the nook's grants and invites in Workspaces stay behind, harmlessly.
        nook.Output.Delete();
        Result saved = await db.SaveAsync(ct);

        lifecycle.Wake();
        return saved;
    }
}
