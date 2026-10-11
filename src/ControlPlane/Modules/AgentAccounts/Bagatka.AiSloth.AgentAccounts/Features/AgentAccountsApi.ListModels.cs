using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Catalog;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Sdk.Anthropic;
using Bagatka.Sdk.GitHub;
using Bagatka.Sdk.OpenAI;
using Bagatka.Sdk.OpenRouter;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    // OpenAI lists every model its API serves; an agent runs on its GPT, o-series, and Codex models,
    // never on those for audio, images, speech, search, embeddings, or moderation.
    // The Codex the nook images run (src/Daemon/Dockerfile: codex-acp 2.1.1), whose models ChatGPT's
    // Codex backend lists.
    private const string NookCodexVersion = "0.159.3";

    private static readonly string[] NotForAgents = ["audio", "realtime", "image", "transcribe", "tts", "search", "embedding", "moderation", "instruct"];

    public async Task<Result<IReadOnlyList<AgentModel>>> ListModelsAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        // Asking the vendor takes what an agent's calls take, a plan's token renewed when it's due.
        Result<AgentAccount> usable = await UsableAsync(actor, id, workspaceId, ct);
        if (usable.Failed)
        {
            return new Result<IReadOnlyList<AgentModel>>(usable.Error);
        }

        // The vendor's list says what the account can use, and what the vendor states of each model
        // stands over the catalog's word.
        IReadOnlyList<Offered> offered = await OfferedAsync(usable.Output.ToCredential(box, settings), ct);
        CatalogPricing pricing = usable.Output.Pricing();
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

        CatalogPricing pricing = account.Pricing();
        ModelsDev.Model? cataloged = await catalog.FindAsync(pricing.Vendor, model, ct);
        return new Result<ModelFacts>(ModelCatalog.Facts(pricing.Vendor, model, cataloged, pricing.Estimate, pricing.Priced));
    }

    // The vendor's list of the account's models, with what the vendor states of each.
    private async Task<IReadOnlyList<Offered>> OfferedAsync(AgentAccountCredential used, CancellationToken ct)
    {
        // A Claude plan's token, which goes to its harness, lists the plan's own models.
        if (used.Access is HarnessToken token)
        {
            return await ClaudePlanModelsAsync(token.Value, ct);
        }

        if (used.Access is not ModelEndpoint endpoint)
        {
            return [];
        }

        // OpenRouter's catalog is public; batch variants are for batch jobs, never an agent's calls.
        if (used.Kind == AgentAccountKind.OpenRouterApiKey)
        {
            IReadOnlyList<OpenRouterModel> listed = await openRouter.ListModelsAsync(ct);
            return [.. listed.Where(model => !model.Id.EndsWith(":batch", StringComparison.Ordinal)).Select(Offered.Of)];
        }

        // A Copilot plan's models are Copilot's to list, for the plan.
        if (used.Kind == AgentAccountKind.CopilotPlan)
        {
            IReadOnlyList<CopilotModel> listed = await copilot.ListModelsAsync(endpoint.Url, endpoint.Headers, ct);
            return [.. listed.Select(Offered.Of)];
        }

        // A Codex plan's models are those ChatGPT's Codex backend offers the Codex the nooks run.
        if (used.Kind == AgentAccountKind.CodexPlan)
        {
            IReadOnlyList<OpenAIModel> codexModels = await openAI.ListCodexModelsAsync(endpoint.Url, endpoint.Headers, NookCodexVersion, ct);
            return [.. codexModels.Select(model => new Offered(model.Id, model.DisplayName ?? model.Id, UsesTools: true, CodexFacts(model)))];
        }

        if (used.Kind == AgentAccountKind.AnthropicApiKey)
        {
            IReadOnlyList<AnthropicModel> served = await anthropic.ListModelsAsync(endpoint.Url, endpoint.Headers, ct);
            return [.. served.Select(model => new Offered(model.Id, model.DisplayName, UsesTools: true, VendorFacts: null))];
        }

        // A ChatGPT plan's catalog holds the models it runs, named and in order; an OpenAI API key's
        // list holds every model the API serves, by ID.
        IReadOnlyList<OpenAIModel> openAIModels = await openAI.ListModelsAsync(endpoint.Url, endpoint.Headers, ct);
        if (used.Kind == AgentAccountKind.ChatGptPlan)
        {
            return [.. openAIModels.Select(model => new Offered(model.Id, model.DisplayName ?? model.Id, UsesTools: true, VendorFacts: null))];
        }

        return [.. openAIModels
            .OrderByDescending(model => model.Created)
            .Select(model => new Offered(model.Id, model.Id, RunsAgents(model.Id), VendorFacts: null))];
    }

    // Not handled: a Claude plan's models (off unless Anthropic allowed this host) are asked of
    // Anthropic's API with the plan's sign-in token, which no test or plan here confirms.
    private async Task<IReadOnlyList<Offered>> ClaudePlanModelsAsync(string token, CancellationToken ct)
    {
        Dictionary<string, string> signedIn = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Authorization"] = "Bearer " + token,
            ["anthropic-beta"] = "oauth-2025-04-20",
        };
        IReadOnlyList<AnthropicModel> served = await anthropic.ListModelsAsync(settings.ClaudeApi, signedIn, ct);
        return [.. served.Select(model => new Offered(model.Id, model.DisplayName, UsesTools: true, VendorFacts: null))];
    }

    // What the Codex backend states of a model: how hard it reasons, and its context.
    private static ModelFacts CodexFacts(OpenAIModel model)
    {
        return new ModelFacts(model.ReasoningEfforts.Count > 0 ? true : null, model.ReasoningEfforts, model.ContextWindow, MaxOutput: null, Price: null, CacheLifetime: null);
    }

    private static bool RunsAgents(string model)
    {
        bool family = model.StartsWith("gpt-", StringComparison.Ordinal)
            || model.StartsWith("codex", StringComparison.Ordinal)
            || (model.Length > 1 && model[0] == 'o' && char.IsAsciiDigit(model[1]));
        return family && !NotForAgents.Any(part => model.Contains(part, StringComparison.Ordinal));
    }
}
