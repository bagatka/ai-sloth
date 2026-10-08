using Bagatka.AiSloth.AgentAccounts.Data;
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

        await using AgentAccountsDbContext db = await databases.CreateDbContextAsync(ct);
        db.Accounts.Add(added.Output);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            return new Result<AgentAccountSummary>(saved.Error);
        }

        return new Result<AgentAccountSummary>(added.Output.ToSummary());
    }

    // Plans are for one person, some need the deployment's permission, and some are added by signing in.
    private Error? Refusal(AddAgentAccount command)
    {
        if (!KindRules.Known(command.Kind))
        {
            return Error.Validation("kind", "Unknown kind of account.");
        }

        if (KindRules.AddedBySignIn(command.Kind))
        {
            return Error.Validation("kind", "This kind of account is added by signing in at its vendor.");
        }

        if (!KindRules.Allowed(command.Kind, settings))
        {
            return Error.Validation("kind", "This host doesn't allow this kind of account.");
        }

        bool sharedPlan = KindRules.PersonalOnly(command.Kind) && command.WorkspaceId is not null;
        return sharedPlan ? Error.Validation("kind", "A plan is for one person: add it as your own account.") : null;
    }
}
