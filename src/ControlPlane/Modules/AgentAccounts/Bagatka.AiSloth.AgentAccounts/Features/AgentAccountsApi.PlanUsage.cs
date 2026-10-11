using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Sdk.Anthropic;
using Bagatka.Sdk.GitHub;
using Bagatka.Sdk.OpenAI;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result<PlanUsage>> GetPlanUsageAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        // Asking the vendor takes what an agent's calls take, a ChatGPT plan's token renewed when due.
        Result<AgentAccount> usable = await UsableAsync(actor, id, workspaceId, ct);
        if (usable.Failed)
        {
            return new Result<PlanUsage>(usable.Error);
        }

        AgentAccount account = usable.Output;
        switch (account.Kind)
        {
            case AgentAccountKind.ChatGptPlan:
                PlanSession session = account.Session(box)!;
                string? accountId = session.IdToken is null ? null : ChatGptSignInClient.ReadIdToken(session.IdToken)?.AccountId;
                IReadOnlyList<ChatGptUsageWindow> windows = await chatGptUsage.GetWindowsAsync(settings.ChatGptBackend, session.AccessToken, accountId, ct);
                return new Result<PlanUsage>(new PlanUsage(PlanMeters.OfChatGpt(windows), time.GetUtcNow()));
            case AgentAccountKind.CopilotPlan:
                CopilotQuota quota = await github.GetCopilotQuotaAsync(account.PlanToken(box), ct);
                return new Result<PlanUsage>(new PlanUsage(PlanMeters.OfCopilot(quota), time.GetUtcNow()));
            case AgentAccountKind.ClaudePlan:
                IReadOnlyList<ClaudePlanWindow> claude = await claudeUsage.GetWindowsAsync(settings.ClaudeApi, account.PlanToken(box), ct);
                return new Result<PlanUsage>(new PlanUsage(PlanMeters.OfClaude(claude), time.GetUtcNow()));
            default:
                return new Result<PlanUsage>(PlanUsage.None);
        }
    }
}
