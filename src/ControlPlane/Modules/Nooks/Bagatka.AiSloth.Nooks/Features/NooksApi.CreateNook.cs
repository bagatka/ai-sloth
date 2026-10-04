using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<NookSummary>> CreateAsync(Actor actor, CreateNook command, CancellationToken ct)
    {
        if (await workspaces.GetRoleAsync(actor, command.WorkspaceId, ct) is null)
        {
            return new Result<NookSummary>(WorkspacesErrors.NotFound);
        }

        if (!providers.Any(provider => string.Equals(provider.Name, command.Provider, StringComparison.Ordinal)))
        {
            return new Result<NookSummary>(Error.Validation("provider", "No sandbox provider has this name."));
        }

        Nook nook = Nook.Create(command.WorkspaceId, command.Provider, time);
        db.Nooks.Add(nook);
        if ((await db.SaveAsync(ct)).IsError(out Error? failed))
        {
            return new Result<NookSummary>(failed);
        }

        reconciler.Wake();
        return new Result<NookSummary>(nook.ToSummary());
    }
}
