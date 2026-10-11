using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.ModelAccess.Catalog;
using Bagatka.Sdk.OpenAI;

namespace Bagatka.ModelAccess.Kinds;

// An OpenAI API key, at OpenAI's API or another endpoint that speaks it. OpenAI lists every model its
// API serves, newest first; an agent runs on its GPT, o-series, and Codex models, never on those for
// audio, images, speech, search, embeddings, or moderation.
internal sealed class OpenAIApiKey(OpenAIModelsClient openAI) : IAccountKind
{
    private static readonly Uri Api = new Uri("https://api.openai.com/v1");
    private static readonly string[] NotForAgents = ["audio", "realtime", "image", "transcribe", "tts", "search", "embedding", "moderation", "instruct"];

    public ModelAccessKind Id => ModelAccessKind.OpenAIApiKey;


    public bool TakesEndpoint => true;

    public CatalogPricing Pricing(bool ownEndpoint)
    {
        return new CatalogPricing(ModelCatalog.OpenAI, Estimate: false, Priced: !ownEndpoint);
    }

    public ModelEndpoint Endpoint(ModelCredential credential, Uri? ownEndpoint)
    {
        return ModelEndpoints.Through(ownEndpoint ?? Api, "Authorization", "Bearer " + credential.Token);
    }

    public async Task<IReadOnlyList<Offered>> OfferedAsync(ModelEndpoint endpoint, CancellationToken ct)
    {
        IReadOnlyList<OpenAIModel> served = await openAI.ListModelsAsync(endpoint.Url, endpoint.Headers, ct);
        return [.. served
            .OrderByDescending(model => model.Created)
            .Select(model => new Offered(model.Id, model.Id, RunsAgents(model.Id), VendorFacts: null))];
    }

    private static bool RunsAgents(string model)
    {
        bool family = model.StartsWith("gpt-", StringComparison.Ordinal)
            || model.StartsWith("codex", StringComparison.Ordinal)
            || (model.Length > 1 && model[0] == 'o' && char.IsAsciiDigit(model[1]));
        return family && !NotForAgents.Any(part => model.Contains(part, StringComparison.Ordinal));
    }
}
