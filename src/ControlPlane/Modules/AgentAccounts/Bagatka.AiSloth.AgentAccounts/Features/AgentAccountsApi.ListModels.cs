using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Catalog;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.AgentAccounts.Kinds;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

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
        IAccountKind kind = kinds.Of(account.Kind);
        IReadOnlyList<Offered> offered = await kind.OfferedAsync(CredentialOf(account).Endpoint, ct);
        CatalogPricing pricing = kind.Pricing(account.Endpoint is not null);
        List<AgentModel> models = [];
        foreach (Offered model in offered)
        {
            ModelsDev.Model? cataloged = await catalog.FindAsync(pricing.Vendor, model.Id, ct);
            ModelFacts facts = ModelCatalog.Facts(pricing.Vendor, model.Id, cataloged, pricing.Estimate, pricing.Priced);
            models.Add(new AgentModel(model.Id, model.Name, model.UsesTools, model.With(facts)));
        }

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

        CatalogPricing pricing = kinds.Of(account.Kind).Pricing(account.Endpoint is not null);
        ModelsDev.Model? cataloged = await catalog.FindAsync(pricing.Vendor, model, ct);
        return new Result<ModelFacts>(ModelCatalog.Facts(pricing.Vendor, model, cataloged, pricing.Estimate, pricing.Priced));
    }
}
