using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Kinds;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public Task<IReadOnlyList<AgentAccountKindSummary>> ListKindsAsync(Actor actor, CancellationToken ct)
    {
        IReadOnlyList<AgentAccountKindSummary> offered = [.. kinds.All.Select(kind => new AgentAccountKindSummary(kind.Id, kind is IPlanKind, kind.Allowed))];
        return Task.FromResult(offered);
    }
}
