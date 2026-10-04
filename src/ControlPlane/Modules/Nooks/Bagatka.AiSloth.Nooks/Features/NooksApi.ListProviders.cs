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

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    public async Task<Result<IReadOnlyList<ProviderSummary>>> ListProvidersAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct)
    {
        if (!(await machines.ListAsync(actor, workspaceId, ct)).TryGetValue(out IReadOnlyList<MachineSummary>? workspaceMachines, out Error? error))
        {
            return new Result<IReadOnlyList<ProviderSummary>>(error);
        }

        List<ProviderSummary> found = providers
            .Where(provider => !string.Equals(provider.Name, MachineProvider.Name, StringComparison.Ordinal))
            .Select(provider => new ProviderSummary(provider.Name, provider.Name, Available: true))
            .ToList();
        found.AddRange(workspaceMachines.Select(machine => new ProviderSummary(
            new ProviderId(MachineProvider.Name, MachineProvider.LocationOf(machine.Id)).ToString(),
            machine.Name,
            machine.Status == MachineStatus.Online)));
        return new Result<IReadOnlyList<ProviderSummary>>(found);
    }
}
