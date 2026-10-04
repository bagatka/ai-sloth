using System;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.AgentAccounts;

// The front door for the contract. Each feature is a file in Features/.
internal sealed partial class AgentAccountsApi(
    AgentAccountsDbContext db,
    IWorkspacesApi workspaces,
    SecretBox box,
    AgentAccountsSettings settings,
    TimeProvider time) : IAgentAccountsApi
{
    private AgentAccountSummary Summary(AgentAccount account)
    {
        return account.ToSummary(settings.SharePersonalAccounts);
    }
}
