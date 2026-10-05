using System;
using Bagatka.AiSloth.Secrets.Contracts;
using Bagatka.AiSloth.Secrets.Data;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.Secrets;

// The front door for the contract. Each feature is a file in Features/.
internal sealed partial class SecretsApi(
    SecretsDbContext db,
    IWorkspacesApi workspaces,
    [FromKeyedServices(SecretsDbContext.Schema)] SecretBox box,
    TimeProvider time) : ISecretsApi
{
}
