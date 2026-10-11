using System;
using System.Linq;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.Sdk.GitHub;
using Bagatka.Sdk.OpenRouter;

namespace Bagatka.AiSloth.AgentAccounts.Catalog;

// A model as its vendor's list offers it, with what the vendor states of it, which stands over the
// catalog's word: Copilot its reasoning efforts and limits, OpenRouter its prices, context, and
// whether it reasons.
internal sealed record Offered(string Id, string Name, bool UsesTools, ModelFacts? VendorFacts)
{
    // OpenRouter prices per token; a price per million reads like the catalog's.
    private const decimal Million = 1_000_000m;

    public static Offered Of(OpenRouterModel model)
    {
        bool reasons = model.SupportedParameters.Contains("reasoning", StringComparer.Ordinal);
        ModelPrice? price = model.Price is OpenRouterPrice paid
            ? new ModelPrice(paid.Prompt * Million, paid.Completion * Million, paid.CacheRead * Million, paid.CacheWrite * Million, [], Estimate: false)
            : null;
        ModelFacts stated = new ModelFacts(reasons ? true : null, [], model.ContextLength, MaxOutput: null, price, CacheLifetime: null);
        return new Offered(model.Id, model.Name, model.SupportedParameters.Contains("tools", StringComparer.Ordinal), stated);
    }

    public static Offered Of(CopilotModel model)
    {
        ModelFacts stated = new ModelFacts(model.ReasoningEfforts.Count > 0 ? true : null, model.ReasoningEfforts, model.ContextWindow, model.MaxOutput, Price: null, CacheLifetime: null);
        return new Offered(model.Id, model.Name, model.UsesTools, stated);
    }

    // The catalog's facts, with what the vendor states in their place. A vendor's price keeps the
    // catalog's tiers, which a flat price doesn't show.
    public ModelFacts With(ModelFacts cataloged)
    {
        if (VendorFacts is not ModelFacts vendor)
        {
            return cataloged;
        }

        ModelPrice? price = vendor.Price is ModelPrice paid ? paid with { Tiers = cataloged.Price?.Tiers ?? [] } : cataloged.Price;
        return new ModelFacts(
            vendor.Reasons ?? cataloged.Reasons,
            vendor.Efforts.Count > 0 ? vendor.Efforts : cataloged.Efforts,
            vendor.ContextWindow ?? cataloged.ContextWindow,
            vendor.MaxOutput ?? cataloged.MaxOutput,
            price,
            cataloged.CacheLifetime);
    }
}
