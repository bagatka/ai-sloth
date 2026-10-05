using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;

namespace Bagatka.AiSloth.AgentAccounts;

internal sealed partial class AgentAccountsApi
{
    public async Task<Result<AgentAccountSummary>> AddAsync(Actor actor, AddAgentAccount command, CancellationToken ct)
    {
        if (actor is not UserActor user)
        {
            return new Result<AgentAccountSummary>(Error.Forbidden);
        }

        if (command.WorkspaceId is not null)
        {
            AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(command.WorkspaceId.Value), ct);
            if (access is null)
            {
                return new Result<AgentAccountSummary>(WorkspacesErrors.NotFound);
            }

            if (access < AccessLevel.Manage)
            {
                return new Result<AgentAccountSummary>(Error.Forbidden);
            }
        }

        Error? refused = Refusal(command);
        if (refused is not null)
        {
            return new Result<AgentAccountSummary>(refused);
        }

        Result<AgentAccountName> name = AgentAccountName.Parse(command.Name);
        if (name.Failed)
        {
            return new Result<AgentAccountSummary>(name.Error);
        }

        ApiEndpoint? endpoint = null;
        if (command.Endpoint is not null)
        {
            Result<ApiEndpoint> parsed = ApiEndpoint.Parse(command.Endpoint);
            if (parsed.Failed)
            {
                return new Result<AgentAccountSummary>(parsed.Error);
            }

            endpoint = parsed.Output;
        }

        UserId? ownerId = command.WorkspaceId is null ? user.UserId : null;
        Result<AgentAccount> added = AgentAccount.Add(command.WorkspaceId, ownerId, command.Kind, name.Output, command.Secret, endpoint, box, time);
        if (added.Failed)
        {
            return new Result<AgentAccountSummary>(added.Error);
        }

        AgentAccount account = added.Output;
        db.Accounts.Add(account);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<AgentAccountSummary>(saved.Error);
        }

        return new Result<AgentAccountSummary>(account.ToSummary());
    }

    // Plans are for one person, Claude subscriptions need the deployment's permission, and ChatGPT
    // plans are added by signing in.
    private Error? Refusal(AddAgentAccount command)
    {
        return command.Kind switch
        {
            AgentAccountKind.ChatGptPlan =>
                Error.Validation("kind", "A ChatGPT plan is added by signing in with ChatGPT."),
            AgentAccountKind.ClaudeSubscription when !settings.AllowClaudeSubscriptions =>
                Error.Validation("kind", "This deployment doesn't allow Claude subscriptions."),
            AgentAccountKind.GitHubCopilotToken or AgentAccountKind.ClaudeSubscription when command.WorkspaceId is not null =>
                Error.Validation("kind", "A plan is for one person: add it as your own account."),
            AgentAccountKind.AnthropicApiKey or AgentAccountKind.OpenAIApiKey or AgentAccountKind.GitHubCopilotToken or AgentAccountKind.ClaudeSubscription => null,
            _ => Error.Validation("kind", "Unknown kind of account."),
        };
    }
}
