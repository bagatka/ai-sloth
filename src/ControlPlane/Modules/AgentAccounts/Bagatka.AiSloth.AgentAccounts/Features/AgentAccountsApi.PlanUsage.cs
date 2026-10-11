using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Kinds;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result<PlanUsage>> GetPlanUsageAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        // Asking the vendor takes what an agent's calls take, a credential renewed when it's due.
        Result<AgentAccount> usable = await UsableAsync(actor, id, workspaceId, ct);
        if (usable.Failed)
        {
            return new Result<PlanUsage>(usable.Error);
        }

        AgentAccount account = usable.Output;
        if (kinds.Of(account.Kind) is not IPlanKind plan)
        {
            return new Result<PlanUsage>(PlanUsage.None);
        }

        IReadOnlyList<UsageMeter> meters = await plan.MetersAsync(account.OpenCredential(box), ct);
        return new Result<PlanUsage>(new PlanUsage(meters, time.GetUtcNow()));
    }
}
