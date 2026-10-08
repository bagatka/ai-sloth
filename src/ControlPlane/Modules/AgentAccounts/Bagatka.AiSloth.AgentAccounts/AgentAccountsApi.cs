using System;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.AgentAccounts.Data;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.OpenAI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.AgentAccounts;

// The front door for the contract. Each feature is a file in Features/.
internal sealed partial class AgentAccountsApi(
    IDbContextFactory<AgentAccountsDbContext> databases,
    IWorkspacesApi workspaces,
    [FromKeyedServices(AgentAccountsDbContext.Schema)] SecretBox box,
    AgentAccountsSettings settings,
    ChatGptSignInClient chatGpt,
    TimeProvider time,
    ILogger<AgentAccountsApi> logger) : IAgentAccountsApi
{
}
