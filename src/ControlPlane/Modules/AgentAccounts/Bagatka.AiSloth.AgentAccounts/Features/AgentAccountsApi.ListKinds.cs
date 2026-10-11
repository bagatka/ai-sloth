using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public Task<IReadOnlyList<AgentAccountKindSummary>> ListKindsAsync(Actor actor, CancellationToken ct)
    {
        IReadOnlyList<AgentAccountKindSummary> offered = [.. vendors.Kinds.Select(kind => new AgentAccountKindSummary(kind, vendors.IsPlan(kind), Allowed(kind)))];
        return Task.FromResult(offered);
    }
}
