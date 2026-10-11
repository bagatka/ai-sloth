using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
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
    private static readonly string[] NotForAgents = ["audio", "realtime", "image", "transcribe", "tts", "search", "embedding", "moderation", "instruct"];

    public async Task<Result<IReadOnlyList<AgentModel>>> ListModelsAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        // Asking the vendor takes what an agent's calls take, a plan's token renewed when it's due.
        Result<AgentAccountCredential> used = await UseAsync(actor, id, workspaceId, ct);
        if (used.Failed)
        {
            return new Result<IReadOnlyList<AgentModel>>(used.Error);
        }

        // A Claude plan's token, which goes to its harness, lists the plan's own models.
        if (used.Output.Access is HarnessToken token)
        {
            IReadOnlyList<AgentModel> planModels = await ClaudePlanModelsAsync(token.Value, ct);
            return new Result<IReadOnlyList<AgentModel>>(planModels);
        }

        if (used.Output.Access is not ModelEndpoint endpoint)
        {
            return new Result<IReadOnlyList<AgentModel>>([]);
        }

        // OpenRouter's catalog is public; batch variants are for batch jobs, never an agent's calls.
        if (used.Output.Kind == AgentAccountKind.OpenRouterApiKey)
        {
            IReadOnlyList<OpenRouterModel> catalog = await openRouter.ListModelsAsync(ct);
            return new Result<IReadOnlyList<AgentModel>>([.. catalog
                .Where(model => !model.Id.EndsWith(":batch", StringComparison.Ordinal))
                .Select(model => new AgentModel(model.Id, model.Name, model.SupportedParameters.Contains("tools", StringComparer.Ordinal)))]);
        }

        // A Copilot plan's models are Copilot's to list, for the plan.
        if (used.Output.Kind == AgentAccountKind.CopilotPlan)
        {
            IReadOnlyList<CopilotModel> offered = await copilot.ListModelsAsync(endpoint.Url, endpoint.Headers, ct);
            return new Result<IReadOnlyList<AgentModel>>([.. offered.Select(model => new AgentModel(model.Id, model.Name, model.UsesTools))]);
        }

        if (used.Output.Kind == AgentAccountKind.AnthropicApiKey)
        {
            IReadOnlyList<AnthropicModel> served = await anthropic.ListModelsAsync(endpoint.Url, endpoint.Headers, ct);
            return new Result<IReadOnlyList<AgentModel>>([.. served.Select(model => new AgentModel(model.Id, model.DisplayName, UsesTools: true))]);
        }

        // A ChatGPT plan's catalog holds the models it runs, named and in order; an OpenAI API key's
        // list holds every model the API serves, by ID.
        IReadOnlyList<OpenAIModel> openAIModels = await openAI.ListModelsAsync(endpoint.Url, endpoint.Headers, ct);
        if (used.Output.Kind == AgentAccountKind.ChatGptPlan)
        {
            return new Result<IReadOnlyList<AgentModel>>([.. openAIModels.Select(model => new AgentModel(model.Id, model.DisplayName ?? model.Id, UsesTools: true))]);
        }

        return new Result<IReadOnlyList<AgentModel>>([.. openAIModels
            .OrderByDescending(model => model.Created)
            .Select(model => new AgentModel(model.Id, model.Id, RunsAgents(model.Id)))]);
    }

    // Not handled: a Claude plan's models (off unless Anthropic allowed this host) are asked of
    // Anthropic's API with the plan's sign-in token, which no test or plan here confirms.
    private async Task<IReadOnlyList<AgentModel>> ClaudePlanModelsAsync(string token, CancellationToken ct)
    {
        Dictionary<string, string> signedIn = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Authorization"] = "Bearer " + token,
            ["anthropic-beta"] = "oauth-2025-04-20",
        };
        IReadOnlyList<AnthropicModel> served = await anthropic.ListModelsAsync(new Uri("https://api.anthropic.com"), signedIn, ct);
        return [.. served.Select(model => new AgentModel(model.Id, model.DisplayName, UsesTools: true))];
    }

    private static bool RunsAgents(string model)
    {
        bool family = model.StartsWith("gpt-", StringComparison.Ordinal)
            || model.StartsWith("codex", StringComparison.Ordinal)
            || (model.Length > 1 && model[0] == 'o' && char.IsAsciiDigit(model[1]));
        return family && !NotForAgents.Any(part => model.Contains(part, StringComparison.Ordinal));
    }
}
