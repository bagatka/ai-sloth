using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Sdk.OpenRouter;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result<IReadOnlyList<AgentModel>>> ListModelsAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        await using AgentAccountsDbContext db = await databases.CreateDbContextAsync(ct);

        AgentAccount? account = await FindInWorkspaceAsync(db, id, workspaceId, ct);
        if (account is null)
        {
            return new Result<IReadOnlyList<AgentModel>>(AgentAccountsErrors.NotFound);
        }

        bool allowed = await AllowedAsync(actor, account, workspaceId, ct);
        if (!allowed)
        {
            return new Result<IReadOnlyList<AgentModel>>(AgentAccountsErrors.NotFound);
        }

        // Not handled: the models of Anthropic, OpenAI, and the plans; listing them takes each vendor's
        // own call, and their agents start on their harness's default model meanwhile.
        if (account.Kind != AgentAccountKind.OpenRouterApiKey)
        {
            return new Result<IReadOnlyList<AgentModel>>([]);
        }

        // Batch variants are for batch jobs, never an agent's calls.
        IReadOnlyList<OpenRouterModel> catalog = await openRouter.ListModelsAsync(ct);
        IReadOnlyList<AgentModel> models = [.. catalog
            .Where(model => !model.Id.EndsWith(":batch", StringComparison.Ordinal))
            .Select(model => new AgentModel(model.Id, model.Name, model.SupportedParameters.Contains("tools", StringComparer.Ordinal)))];
        return new Result<IReadOnlyList<AgentModel>>(models);
    }
}
