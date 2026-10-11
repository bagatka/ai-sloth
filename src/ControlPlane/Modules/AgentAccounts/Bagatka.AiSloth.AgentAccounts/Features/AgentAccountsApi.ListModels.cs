using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.ModelAccess;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result<IReadOnlyList<AgentModel>>> ListModelsAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        // Asking the vendor takes what an agent's calls take, a credential renewed when it's due.
        Result<AgentAccount> usable = await UsableAsync(actor, id, workspaceId, ct);
        if (usable.Failed)
        {
            return new Result<IReadOnlyList<AgentModel>>(usable.Error);
        }

        // The vendor's list says what the account can use, and what the vendor states of each model
        // stands over the catalog's word.
        AgentAccount account = usable.Output;
        IReadOnlyList<AgentModel> models = await vendors.ListModelsAsync(account.Kind, account.OpenCredential(box), account.Endpoint?.Value, ct);
        return new Result<IReadOnlyList<AgentModel>>(models);
    }

    public async Task<Result<ModelFacts>> GetModelFactsAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, string model, CancellationToken ct)
    {
        await using AgentAccountsDbContext db = await databases.CreateDbContextAsync(ct);

        AgentAccount? account = await FindInWorkspaceAsync(db, id, workspaceId, ct);
        if (account is null)
        {
            return new Result<ModelFacts>(AgentAccountsErrors.NotFound);
        }

        bool allowed = await AllowedAsync(actor, account, workspaceId, ct);
        if (!allowed)
        {
            return new Result<ModelFacts>(AgentAccountsErrors.NotFound);
        }

        ModelFacts facts = await vendors.FactsAsync(account.Kind, model, account.Endpoint is not null, ct);
        return new Result<ModelFacts>(facts);
    }
}
