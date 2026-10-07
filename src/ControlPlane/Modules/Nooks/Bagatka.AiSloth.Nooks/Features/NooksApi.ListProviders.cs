using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Sandboxing;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    // What people see the deployment's own provider as, whatever runs it underneath.
    private const string Cloud = "cloud";

    public async Task<Result<IReadOnlyList<ProviderSummary>>> ListProvidersAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct)
    {
        Result<IReadOnlyList<MachineSummary>> workspaceMachines = await machines.ListAsync(actor, workspaceId, ct);
        if (workspaceMachines.Failed)
        {
            return new Result<IReadOnlyList<ProviderSummary>>(workspaceMachines.Error);
        }

        // A deployment with several providers of its own, as in development, shows each by its name.
        List<ISandboxProvider> own = [.. providers.Where(provider => !string.Equals(provider.Name, MachineProvider.Name, StringComparison.Ordinal))];
        List<ProviderSummary> found = [.. own.Select(provider => new ProviderSummary(provider.Name, own.Count == 1 ? Cloud : provider.Name, Available: true))];
        found.AddRange(workspaceMachines.Output.Select(machine => new ProviderSummary(
            new ProviderId(MachineProvider.Name, MachineProvider.LocationOf(machine.Id)).ToString(),
            machine.Name,
            machine.Status == MachineStatus.Online)));
        return new Result<IReadOnlyList<ProviderSummary>>(found);
    }
}
