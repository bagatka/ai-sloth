using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Harnesses;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<IReadOnlyList<AgentModel>>> ListModelsAsync(Actor actor, WorkspaceId workspaceId, string harness, AgentAccountId account, CancellationToken ct)
    {
        Result<UserId> person = await PersonInAsync(actor, workspaceId, AccessLevel.Read, ct);
        if (person.Failed)
        {
            return new Result<IReadOnlyList<AgentModel>>(person.Error);
        }

        Result<HarnessProfile> found = HarnessWith(harness, model: null, thinking: null);
        if (found.Failed)
        {
            return new Result<IReadOnlyList<AgentModel>>(found.Error);
        }

        HarnessProfile profile = found.Output;

        Result<AgentAccountSummary> usable = await AccountForAsync(actor, workspaceId, account, profile, ct);
        if (usable.Failed)
        {
            return new Result<IReadOnlyList<AgentModel>>(usable.Error);
        }

        if (!profile.TakesModel)
        {
            return new Result<IReadOnlyList<AgentModel>>([]);
        }

        // A coding agent works through tools, so a model that can't call them can't run one.
        Result<IReadOnlyList<AgentModel>> models = await accounts.ListModelsAsync(actor, account, workspaceId, ct);
        if (models.Failed)
        {
            return models;
        }

        return new Result<IReadOnlyList<AgentModel>>([.. models.Output.Where(model => model.UsesTools)]);
    }
}
