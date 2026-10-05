using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// The workspace commands use on the current host, and inviting people to it.
internal sealed partial class Sloth
{
    private async Task<int> ListWorkspacesAsync(CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.WorkspacePage workspaces = await api.GetAsync("/workspaces?limit=200", CliJsonContext.Default.WorkspacePage, ct);
        foreach (Wire.Workspace workspace in workspaces.Items)
        {
            string marker = workspace.Id == host.Workspace ? "* " : "  ";
            await terminal.WriteLineAsync(marker + workspace.Name + "  (" + workspace.Access + ")");
        }

        return 0;
    }

    private async Task<int> UseWorkspaceAsync(string name, CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.WorkspacePage workspaces = await api.GetAsync("/workspaces?limit=200", CliJsonContext.Default.WorkspacePage, ct);
        List<Wire.Workspace> named = [.. workspaces.Items.Where(workspace => Names(workspace.Id, workspace.Name, name))];
        if (named is not [Wire.Workspace workspace])
        {
            await terminal.FailAsync(named.Count == 0
                ? "You have no workspace \"" + name + "\" on " + host.Name + ". Yours: sloth workspace list"
                : "Several workspaces are named \"" + name + "\"; use one's ID: " + string.Join(", ", named.Select(found => found.Id)));
            return 1;
        }

        await PrivateFile.WriteAsync(HostsPath, hosts.With(host with { Workspace = workspace.Id, WorkspaceName = workspace.Name }), CliJsonContext.Default.HostsFile, ct);
        await terminal.WriteLineAsync("Commands use the workspace \"" + workspace.Name + "\" now.");
        return 0;
    }

    private async Task<int> InviteAsync(string access, CancellationToken ct)
    {
        string? level = access switch
        {
            "read" => "Read",
            "write" => "Write",
            "manage" => "Manage",
            _ => null,
        };
        if (level is null)
        {
            return await UsageAsync();
        }

        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.Invite invite = await api.SendAsync(
            HttpMethod.Post, "/workspaces/" + workspace + "/invites", new Wire.CreateInvite(level), CliJsonContext.Default.CreateInvite, CliJsonContext.Default.Invite, ct);
        Wire.HostDiscovery discovery = await api.GetAsync("/.well-known/aisloth", CliJsonContext.Default.HostDiscovery, ct);
        await terminal.WriteLineAsync("Invite code " + invite.Code + ": " + level + " on \"" + host.WorkspaceName + "\" for whoever uses it first, within 7 days.");
        await terminal.WriteLineAsync("  Someone on " + host.Name + " already: sloth workspace join " + invite.Code);
        if (discovery.SignIn.InviteSignUp)
        {
            await terminal.WriteLineAsync("  Someone new: sloth host add " + host.Url.AbsoluteUri.TrimEnd('/') + " --code " + invite.Code);
        }

        return 0;
    }

    // Accepts an invite. Joining a workspace makes it the one commands use.
    private async Task<int> JoinAsync(string code, CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.Resource joined = await api.SendAsync(
            HttpMethod.Post, "/invites/accept", new Wire.AcceptInvite(code.Trim()), CliJsonContext.Default.AcceptInvite, CliJsonContext.Default.Resource, ct);
        if (joined.Kind is not "Workspace")
        {
            await terminal.WriteLineAsync("You joined a nook. Follow its chat with the ID its people give you: sloth chat open <id>");
            return 0;
        }

        Wire.WorkspacePage workspaces = await api.GetAsync("/workspaces?limit=200", CliJsonContext.Default.WorkspacePage, ct);
        Wire.Workspace? workspace = workspaces.Items.FirstOrDefault(found => found.Id == joined.Id);
        await PrivateFile.WriteAsync(HostsPath, hosts.With(host with { Workspace = joined.Id, WorkspaceName = workspace?.Name }), CliJsonContext.Default.HostsFile, ct);
        await terminal.WriteLineAsync("You joined \"" + workspace?.Name + "\"; commands use it now.");
        return 0;
    }

    // Whether people mean this by what they typed: its ID, or its name in any case.
    private static bool Names(Guid id, string name, string typed)
    {
        return string.Equals(name, typed, StringComparison.OrdinalIgnoreCase) || string.Equals(id.ToString("D", CultureInfo.InvariantCulture), typed, StringComparison.OrdinalIgnoreCase);
    }
}
