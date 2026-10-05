using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public Task<IReadOnlyList<AgentAccountKindSummary>> ListKindsAsync(Actor actor, CancellationToken ct)
    {
        IReadOnlyList<AgentAccountKindSummary> kinds = [.. Enum.GetValues<AgentAccountKind>().Select(kind => new AgentAccountKindSummary(
            kind,
            KindRules.AddedBySignIn(kind),
            KindRules.PersonalOnly(kind),
            KindRules.Allowed(kind, settings)))];
        return Task.FromResult(kinds);
    }
}
