using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// Secrets: environment variables every process started in the workspace's nooks gets, agents
// included. Values are typed hidden or read from standard input, and never shown again.
internal sealed partial class Sloth
{
    private async Task<int> ListSecretsAsync(CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        IReadOnlyList<Wire.Secret> secrets = await api.GetAsync("/workspaces/" + workspace + "/secrets", CliJsonContext.Default.IReadOnlyListSecret, ct);
        if (secrets.Count == 0)
        {
            await terminal.WriteLineAsync("No secrets yet. Set one: sloth secret set <NAME>");
            return 0;
        }

        int width = secrets.Max(secret => secret.Name.Length);
        foreach (Wire.Secret secret in secrets)
        {
            await terminal.WriteLineAsync(secret.Name.PadRight(width) + "  set " + Ago(secret.SetAt));
        }

        return 0;
    }

    private async Task<int> SetSecretAsync(string name, CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        string? value = await terminal.ReadSecretAsync("Value of " + name, ct);
        using HostApi api = ApiFor(host);
        Wire.Secret secret = await api.SendAsync(
            HttpMethod.Put,
            "/workspaces/" + workspace + "/secrets/" + Uri.EscapeDataString(name),
            new Wire.SetSecret(value ?? string.Empty),
            CliJsonContext.Default.SetSecret,
            CliJsonContext.Default.Secret,
            ct);
        await terminal.WriteLineAsync("Every process started in \"" + host.WorkspaceName + "\"'s nooks from now on gets " + secret.Name + ".");
        return 0;
    }

    private async Task<int> RemoveSecretAsync(string name, CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        await api.CallAsync(HttpMethod.Delete, "/workspaces/" + workspace + "/secrets/" + Uri.EscapeDataString(name), ct);
        await terminal.WriteLineAsync("Removed " + name + "; processes already running keep it.");
        return 0;
    }
}
