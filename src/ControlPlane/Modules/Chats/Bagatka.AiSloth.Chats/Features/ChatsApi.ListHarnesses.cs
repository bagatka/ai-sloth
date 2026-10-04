using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Chats.Harness;
using Bagatka.Foundation;
using Bagatka.Harnesses;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public Task<IReadOnlyList<HarnessSummary>> ListHarnessesAsync(Actor actor, CancellationToken ct)
    {
        AgentAccountKind[] kinds = Enum.GetValues<AgentAccountKind>();
        IReadOnlyList<HarnessSummary> harnesses = [.. HarnessProfiles.All.Select(harness => new HarnessSummary(
            harness.Id,
            harness.Name,
            [.. kinds.Where(kind => harness.Accepts(AccountCredentials.KindOf(kind)))]))];
        return Task.FromResult(harnesses);
    }
}
