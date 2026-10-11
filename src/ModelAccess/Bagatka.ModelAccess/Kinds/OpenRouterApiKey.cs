using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.ModelAccess.Catalog;
using Bagatka.Sdk.OpenRouter;

namespace Bagatka.ModelAccess.Kinds;

// An OpenRouter key: many vendors' models with one key. OpenRouter serves Anthropic's Messages API at
// its root (/v1/messages) and OpenAI's at /v1, so each harness calls the API it speaks. Its catalog is
// public; batch variants are for batch jobs, never an agent's calls.
internal sealed class OpenRouterApiKey(ModelAccessSettings settings, OpenRouterClient openRouter) : IAccountKind
{
    public ModelAccessKind Id => ModelAccessKind.OpenRouterApiKey;


    public bool TakesEndpoint => false;

    public CatalogPricing Pricing(bool ownEndpoint)
    {
        return new CatalogPricing(ModelCatalog.OpenRouter, Estimate: false, Priced: true);
    }

    public ModelEndpoint Endpoint(ModelCredential credential, Uri? ownEndpoint)
    {
        return ModelEndpoints.Through(settings.OpenRouterApi, "Authorization", "Bearer " + credential.Token);
    }

    public async Task<IReadOnlyList<Offered>> OfferedAsync(ModelEndpoint endpoint, CancellationToken ct)
    {
        IReadOnlyList<OpenRouterModel> listed = await openRouter.ListModelsAsync(ct);
        return [.. listed.Where(model => !model.Id.EndsWith(":batch", StringComparison.Ordinal)).Select(Offered.Of)];
    }
}
