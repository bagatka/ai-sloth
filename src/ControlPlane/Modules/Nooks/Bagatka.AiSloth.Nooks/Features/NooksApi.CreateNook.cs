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
        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(command.WorkspaceId), ct);
        if (access is null)
        {
            return new Result<NookSummary>(WorkspacesErrors.NotFound);
        }

        if (access < AccessLevel.Write)
        {
            return new Result<NookSummary>(Error.Forbidden);
        }

        ProviderId? provider = await FindProviderAsync(actor, command.WorkspaceId, command.Provider, ct);
        if (provider is null)
        {
            return new Result<NookSummary>(Error.Validation("provider", "The workspace has no provider with this ID."));
        }

        bool harnessOffered = command.Harness is null || settings.HarnessImages.ContainsKey(command.Harness);
        if (!harnessOffered)
        {
            return new Result<NookSummary>(Error.Validation("harness", "This deployment offers no harness with this ID."));
        }

        Nook nook = Nook.Create(command.WorkspaceId, provider.Value, command.Harness, time);

        // Recorded before the nook is saved: a record for a nook that never got saved stands alone harmlessly.
        AddResource inWorkspace = new AddResource(Resource.Nook(nook.Id.Value), Resource.Workspace(command.WorkspaceId));
        Result added = await workspaces.AddResourceAsync(actor, inWorkspace, ct);
        if (added.Failed)
        {
            return new Result<NookSummary>(added.Error);
        }

        db.Nooks.Add(nook);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<NookSummary>(saved.Error);
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

        MachineId? machineId = MachineProvider.ParseLocation(provider.Location);
        if (machineId is null)
        {
            return null;
        }

        Result<MachineSummary> machine = await machines.GetAsync(actor, machineId.Value, ct);
        bool workspaceMachine = !machine.Failed && machine.Output.WorkspaceId == workspaceId;
        return workspaceMachine ? provider with { Location = MachineProvider.LocationOf(machineId.Value) } : null;
    }
}
