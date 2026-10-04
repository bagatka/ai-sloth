using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
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

        if (await FindProviderAsync(actor, command.WorkspaceId, command.Provider, ct) is not ProviderId provider)
        {
            return new Result<NookSummary>(Error.Validation("provider", "The workspace has no provider with this ID."));
        }

        Nook nook = Nook.Create(command.WorkspaceId, provider, time);
        db.Nooks.Add(nook);
        if ((await db.SaveAsync(ct)).IsError(out Error? failed))
        {
            return new Result<NookSummary>(failed);
        }

        reconciler.Wake();
        return new Result<NookSummary>(nook.ToSummary());
    }

    // The provider, if the workspace has it. Nooks run only on their own workspace's machines.
    private async Task<ProviderId?> FindProviderAsync(Actor actor, WorkspaceId workspaceId, string id, CancellationToken ct)
    {
        ProviderId provider = ProviderId.Parse(id);
        if (!providers.Any(candidate => string.Equals(candidate.Name, provider.Name, StringComparison.Ordinal)))
        {
            return null;
        }

        if (!string.Equals(provider.Name, MachineProvider.Name, StringComparison.Ordinal))
        {
            return provider.Location is null ? provider : null;
        }

        return MachineProvider.TryParseLocation(provider.Location, out MachineId machineId)
            && (await machines.GetAsync(actor, machineId, ct)).TryGetValue(out MachineSummary? machine, out _)
            && machine.WorkspaceId == workspaceId
            ? provider with { Location = MachineProvider.LocationOf(machineId) }
            : null;
    }
}
