using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Sdk.Anthropic;
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

        // Not handled: the models of Copilot and Claude plans, whose token goes to their harness; their
        // agents start on its default model.
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

        if (used.Output.Kind == AgentAccountKind.AnthropicApiKey)
        {
            IReadOnlyList<AnthropicModel> served = await anthropic.ListModelsAsync(endpoint.Url, endpoint.Headers, ct);
            return new Result<IReadOnlyList<AgentModel>>([.. served.Select(model => new AgentModel(model.Id, model.DisplayName, UsesTools: true))]);
        }

        // An OpenAI API key or a ChatGPT plan. A plan's list isn't what it may use: it may run models
        // the API doesn't list, which a chat can still name.
        IReadOnlyList<OpenAIModel> openAIModels = await openAI.ListModelsAsync(endpoint.Url, endpoint.Headers, ct);
        return new Result<IReadOnlyList<AgentModel>>([.. openAIModels
            .OrderByDescending(model => model.Created)
            .Select(model => new AgentModel(model.Id, model.Id, RunsAgents(model.Id)))]);
    }

    private static bool RunsAgents(string model)
    {
        bool family = model.StartsWith("gpt-", StringComparison.Ordinal)
            || model.StartsWith("codex", StringComparison.Ordinal)
            || (model.Length > 1 && model[0] == 'o' && char.IsAsciiDigit(model[1]));
        return family && !NotForAgents.Any(part => model.Contains(part, StringComparison.Ordinal));
    }
}
