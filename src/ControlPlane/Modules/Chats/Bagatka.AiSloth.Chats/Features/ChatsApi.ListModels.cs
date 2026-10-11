using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Harnesses;

namespace Bagatka.AiSloth.Chats;

internal sealed partial class ChatsApi
{
    public async Task<Result<IReadOnlyList<HarnessModel>>> ListModelsAsync(Actor actor, WorkspaceId workspaceId, string harness, AgentAccountId account, CancellationToken ct)
    {
        Result<UserId> person = await PersonInAsync(actor, workspaceId, AccessLevel.Read, ct);
        if (person.Failed)
        {
            return new Result<IReadOnlyList<HarnessModel>>(person.Error);
        }

        Result<HarnessProfile> found = FindHarness(harness);
        if (found.Failed)
        {
            return new Result<IReadOnlyList<HarnessModel>>(found.Error);
        }

        HarnessProfile profile = found.Output;

        Result<AgentAccountSummary> usable = await AccountForAsync(actor, workspaceId, account, profile, ct);
        if (usable.Failed)
        {
            return new Result<IReadOnlyList<HarnessModel>>(usable.Error);
        }

        // A coding agent works through tools, so a model that can't call them can't run one.
        Result<IReadOnlyList<AgentModel>> models = await accounts.ListModelsAsync(actor, account, workspaceId, ct);
        if (models.Failed)
        {
            return new Result<IReadOnlyList<HarnessModel>>(models.Error);
        }

        // The models the harness is made for come first, in the vendor's order, so a new chat starts on
        // one of them: Claude Code on Claude, Codex on GPT, wherever an account lists others too.
        string? family = profile.ModelFamily;
        IEnumerable<AgentModel> toolUsers = models.Output.Where(model => model.UsesTools);
        return new Result<IReadOnlyList<HarnessModel>>([.. toolUsers
            .OrderBy(model => family is not null && model.Id.Contains(family, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .Select(model => new HarnessModel(model.Id, model.Name, profile.LevelsFor(model.Facts.Reasons, model.Facts.Efforts), model.Facts))]);
    }
}
