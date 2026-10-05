using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// A workspace's repositories: what its chats' nooks start with, each at /work/<name>.
internal sealed partial class Sloth
{
    private async Task<int> ListRepositoriesAsync(CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        IReadOnlyList<Wire.Repository> repositories = await api.GetAsync("/workspaces/" + workspace + "/repositories", CliJsonContext.Default.IReadOnlyListRepository, ct);
        if (repositories.Count == 0)
        {
            await terminal.WriteLineAsync("No repositories yet. Add one: sloth repo add <owner/name>");
            return 0;
        }

        int width = repositories.Max(repository => repository.Name.Length);
        foreach (Wire.Repository repository in repositories)
        {
            await terminal.WriteLineAsync(repository.Name.PadRight(width) + "  " + repository.FullName + "  (" + repository.DefaultBranch + ")");
        }

        return 0;
    }

    // With a name, adds it to the workspace; without one, lists those the person's GitHub connection reaches.
    private async Task<int> AddRepositoryAsync(string[] words, CancellationToken ct)
    {
        if (words.Length > 1)
        {
            return await UsageAsync();
        }

        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        if (words is not [string fullName])
        {
            Wire.AvailableRepositories available = await api.GetAsync("/github/repositories", CliJsonContext.Default.AvailableRepositories, ct);
            await terminal.WriteLineAsync(available.Repositories.Count == 0 ? "Your GitHub connection reaches no repositories yet." : "Add one with: sloth repo add <owner/name>");
            foreach (Wire.AvailableRepository repository in available.Repositories)
            {
                await terminal.WriteLineAsync("  " + repository.FullName + (repository.Private ? "  (private)" : string.Empty));
            }

            await terminal.WriteLineAsync("Install the app on more of your repositories: " + available.InstallUrl);
            return 0;
        }

        Wire.Repository added = await api.SendAsync(
            HttpMethod.Post, "/workspaces/" + workspace + "/repositories", new Wire.AddRepository(fullName), CliJsonContext.Default.AddRepository, CliJsonContext.Default.Repository, ct);
        await terminal.WriteLineAsync("Added " + added.FullName + " to \"" + host.WorkspaceName + "\". Start a chat with it: sloth chat \"<message>\" --repo " + added.Name);
        return 0;
    }

    private async Task<int> RemoveRepositoryAsync(string name, CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        IReadOnlyList<Wire.Repository> repositories = await api.GetAsync("/workspaces/" + workspace + "/repositories", CliJsonContext.Default.IReadOnlyListRepository, ct);
        Wire.Repository? repository = repositories.FirstOrDefault(found => Names(found.Id, found.Name, name) || string.Equals(found.FullName, name, StringComparison.OrdinalIgnoreCase));
        if (repository is null)
        {
            await terminal.FailAsync("\"" + host.WorkspaceName + "\" has no repository " + name + ". Its repositories: sloth repo list");
            return 1;
        }

        await api.CallAsync(HttpMethod.Delete, "/repositories/" + repository.Id, ct);
        await terminal.WriteLineAsync("Removed " + repository.FullName + "; nooks that have it keep their copy.");
        return 0;
    }

    // The workspace's repositories named `name` or `name@branch`, as a chat's nook starts with them;
    // null after saying which one isn't there.
    private async Task<List<Wire.NookRepository>?> ResolveRepositoriesAsync(HostApi api, SignedInHost host, Guid workspace, IReadOnlyList<string> wanted, CancellationToken ct)
    {
        if (wanted.Count == 0)
        {
            return [];
        }

        IReadOnlyList<Wire.Repository> repositories = await api.GetAsync("/workspaces/" + workspace + "/repositories", CliJsonContext.Default.IReadOnlyListRepository, ct);
        List<Wire.NookRepository> resolved = [];
        foreach (string asked in wanted)
        {
            string[] parts = asked.Split('@', 2);
            Wire.Repository? repository = repositories.FirstOrDefault(found => string.Equals(found.Name, parts[0], StringComparison.OrdinalIgnoreCase));
            if (repository is null)
            {
                await terminal.FailAsync("\"" + host.WorkspaceName + "\" has no repository " + parts[0] + ". Add it: sloth repo add <owner/name>");
                return null;
            }

            resolved.Add(new Wire.NookRepository(repository.Id, parts.Length > 1 ? parts[1] : null));
        }

        return resolved;
    }
}
