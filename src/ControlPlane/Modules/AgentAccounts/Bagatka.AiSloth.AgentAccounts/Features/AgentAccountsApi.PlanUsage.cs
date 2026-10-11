using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.Anthropic;
using Bagatka.Sdk.GitHub;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result<PlanUsage>> GetPlanUsageAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, CancellationToken ct)
    {
        await using AgentAccountsDbContext db = await databases.CreateDbContextAsync(ct);

        AgentAccount? account = await FindInWorkspaceAsync(db, id, workspaceId, ct);
        if (account is null)
        {
            return new Result<PlanUsage>(AgentAccountsErrors.NotFound);
        }

        bool allowed = await AllowedAsync(actor, account, workspaceId, ct);
        if (!allowed)
        {
            return new Result<PlanUsage>(AgentAccountsErrors.NotFound);
        }

        if (account.NeedsSignIn)
        {
            return new Result<PlanUsage>(AgentAccountsErrors.SignInEnded);
        }

        // A ChatGPT plan's limits come with its answers; Copilot and Anthropic are asked.
        switch (account.Kind)
        {
            case AgentAccountKind.ChatGptPlan:
                return new Result<PlanUsage>(new PlanUsage(PlanMeters.OfChatGpt(account.ReportedLimits), account.LimitsReportedAt));
            case AgentAccountKind.CopilotPlan:
                CopilotQuota quota = await github.GetCopilotQuotaAsync(account.PlanToken(box), ct);
                return new Result<PlanUsage>(new PlanUsage(PlanMeters.OfCopilot(quota), time.GetUtcNow()));
            case AgentAccountKind.ClaudePlan:
                IReadOnlyList<ClaudePlanWindow> windows = await claudeUsage.GetWindowsAsync(settings.ClaudeApi, account.PlanToken(box), ct);
                return new Result<PlanUsage>(new PlanUsage(PlanMeters.OfClaude(windows), time.GetUtcNow()));
            default:
                return new Result<PlanUsage>(PlanUsage.None);
        }
    }

    public async Task<Result> ObservePlanUsageAsync(Actor actor, AgentAccountId id, WorkspaceId workspaceId, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        await using AgentAccountsDbContext db = await databases.CreateDbContextAsync(ct);

        AgentAccount? account = await FindInWorkspaceAsync(db, id, workspaceId, ct);
        if (account is null)
        {
            return new Result(AgentAccountsErrors.NotFound);
        }

        bool allowed = await AllowedAsync(actor, account, workspaceId, ct);
        if (!allowed)
        {
            return new Result(AgentAccountsErrors.NotFound);
        }

        string? report = PlanMeters.ChatGptReport(headers);
        if (account.Kind != AgentAccountKind.ChatGptPlan || report is null)
        {
            return new Result(new Success());
        }

        db.Accounts.Attach(account);
        account.LimitsReported(report, time);
        return await db.SaveAsync(ct);
    }
}
