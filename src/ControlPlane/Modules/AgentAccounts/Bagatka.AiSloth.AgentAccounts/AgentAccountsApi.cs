using System;
using System.Collections.Generic;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.OpenAI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.AgentAccounts;

// The front door for the contract. Each feature is a file in Features/.
internal sealed partial class AgentAccountsApi(
    IDbContextFactory<AgentAccountsDbContext> databases,
    IWorkspacesApi workspaces,
    [FromKeyedServices(AgentAccountsDbContext.Schema)] SecretBox box,
    AgentAccountsSettings settings,
    ChatGptSignInClient chatGpt,
    IProductEvents productEvents,
    TimeProvider time,
    ILogger<AgentAccountsApi> logger) : IAgentAccountsApi
{
    // An account someone added, for product analytics: its kind, and whether it's their workspace's.
    private void CaptureAdded(UserId by, AgentAccount account)
    {
        productEvents.Capture(new ProductEvent("agent_account_added", by, account.WorkspaceId?.Value, new Dictionary<string, ProductFact>(StringComparer.Ordinal)
        {
            ["kind"] = new ProductFact(account.Kind.ToString()),
            ["workspace_account"] = new ProductFact(account.WorkspaceId is not null),
        }));
    }
}
