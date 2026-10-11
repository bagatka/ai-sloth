using System;
using System.Collections.Generic;
using System.Globalization;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.AgentAccounts.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.ModelAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.AgentAccounts;

// The front door for the contract. Each feature is a file in Features/, the same for every kind of
// account; what differs between kinds is theirs (Kinds/).
internal sealed partial class AgentAccountsApi(
    IDbContextFactory<AgentAccountsDbContext> databases,
    IWorkspacesApi workspaces,
    [FromKeyedServices(AgentAccountsDbContext.Schema)] SecretBox box,
    AgentAccountsSettings settings,
    ModelVendors vendors,
    IProductEvents productEvents,
    TimeProvider time,
    ILogger<AgentAccountsApi> logger) : IAgentAccountsApi
{
    // Plans whose vendors allow hosted apps only by agreement are off until the deployment turns them on.
    private bool Allowed(ModelAccessKind kind)
    {
        return kind switch
        {
            ModelAccessKind.ClaudePlan => settings.AllowClaudePlans,
            ModelAccessKind.ChatGptPlan => settings.AllowChatGptPlans,
            ModelAccessKind.AnthropicApiKey or ModelAccessKind.OpenAIApiKey or ModelAccessKind.OpenRouterApiKey or ModelAccessKind.CopilotPlan => true,
        };
    }

    // What ties the code a vendor's page shows the person to its sign-in: the sign-in's ID.
    private static string StateOf(SignInId id)
    {
        return id.Value.ToString("N", CultureInfo.InvariantCulture);
    }

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
