using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Catalog;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.Sdk.Anthropic;

namespace Bagatka.AiSloth.AgentAccounts.Kinds;

// An Anthropic API key, at Anthropic's API or another endpoint that speaks it, such as a company's
// gateway. Its models are the API's list there.
internal sealed class AnthropicApiKey(AgentAccountsSettings settings, AnthropicModelsClient anthropic) : IAccountKind
{
    public AgentAccountKind Id => AgentAccountKind.AnthropicApiKey;

    public bool Allowed => true;

    public bool TakesEndpoint => true;

    public CatalogPricing Pricing(bool ownEndpoint)
    {
        return new CatalogPricing(ModelCatalog.Anthropic, Estimate: false, Priced: !ownEndpoint);
    }

    public ModelEndpoint Endpoint(Credential credential, Uri? ownEndpoint)
    {
        return ModelEndpoints.Through(ownEndpoint ?? settings.AnthropicApi, "x-api-key", credential.Token);
    }

    public async Task<IReadOnlyList<Offered>> OfferedAsync(ModelEndpoint endpoint, CancellationToken ct)
    {
        IReadOnlyList<AnthropicModel> served = await anthropic.ListModelsAsync(endpoint.Url, endpoint.Headers, ct);
        return [.. served.Select(Offered.Of)];
    }
}
