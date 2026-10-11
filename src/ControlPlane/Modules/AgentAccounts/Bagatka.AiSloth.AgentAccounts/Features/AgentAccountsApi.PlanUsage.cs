using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.ModelAccess;

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
        if (!vendors.IsPlan(account.Kind))
        {
            return new Result<PlanUsage>(PlanUsage.None);
        }

        IReadOnlyList<UsageMeter> meters = await vendors.LimitsAsync(account.Kind, account.OpenCredential(box), ct);
        return new Result<PlanUsage>(new PlanUsage(meters, time.GetUtcNow()));
    }
}
