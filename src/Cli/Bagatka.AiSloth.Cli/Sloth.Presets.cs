using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// Presets: the agents someone starts chats with most, each a harness, the account that pays, and the
// model; and the models an agent can run on with an account, to choose one from.
internal sealed partial class Sloth
{
    private async Task<int> ListPresetsAsync(CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        IReadOnlyList<Wire.Preset> presets = await api.GetAsync("/workspaces/" + workspace + "/presets", CliJsonContext.Default.IReadOnlyListPreset, ct);
        if (presets.Count == 0)
        {
            await terminal.WriteLineAsync("No presets yet. Save one: sloth preset add --harness <id> --account <name> --model <id>");
            return 0;
        }

        IReadOnlyList<Wire.Account> accounts = await api.GetAsync("/workspaces/" + workspace + "/agent-accounts", CliJsonContext.Default.IReadOnlyListAccount, ct);
        int width = presets.Max(preset => preset.Name.Length);
        foreach (Wire.Preset preset in presets)
        {
            string account = accounts.FirstOrDefault(found => found.Id == preset.Account)?.Name ?? "an account";
            string thinking = preset.Thinking is null ? string.Empty : " · " + preset.Thinking;
            await terminal.WriteLineAsync(preset.Name.PadRight(width) + "  " + preset.Harness + " · " + (preset.Model ?? "default model") + thinking + " · " + account);
        }

        return 0;
    }

    private async Task<int> AddPresetAsync(string[] words, CancellationToken ct)
    {
        CommandLine? line = CommandLine.Parse(words, ["--harness", "--account", "--model", "--thinking", "--name"], []);
        if (line is not { Arguments: [] })
        {
            return await UsageAsync();
        }

        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        AgentChoice? agent = await ChooseAgentAsync(api, host, workspace, line.Value("--account"), line.Value("--harness"), ct);
        if (agent is null)
        {
            return 1;
        }

        Wire.Preset preset = await api.SendAsync(
            HttpMethod.Post,
            "/workspaces/" + workspace + "/presets",
            new Wire.AddPreset(agent.Harness.Id, agent.Account.Id, line.Value("--model"), line.Value("--thinking"), line.Value("--name")),
            CliJsonContext.Default.AddPreset,
            CliJsonContext.Default.Preset,
            ct);
        await terminal.WriteLineAsync("Saved \"" + preset.Name + "\". Start a chat with it: sloth chat --preset \"" + preset.Name + "\"");
        return 0;
    }

    private async Task<int> RenamePresetAsync(string named, string name, CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.Preset? preset = await FindPresetAsync(api, workspace, named, ct);
        if (preset is null)
        {
            return 1;
        }

        Wire.Preset renamed = await api.SendAsync(
            HttpMethod.Put, "/presets/" + preset.Id + "/name", new Wire.PresetName(name), CliJsonContext.Default.PresetName, CliJsonContext.Default.Preset, ct);
        await terminal.WriteLineAsync("Renamed it \"" + renamed.Name + "\".");
        return 0;
    }

    private async Task<int> RemovePresetAsync(string named, CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.Preset? preset = await FindPresetAsync(api, workspace, named, ct);
        if (preset is null)
        {
            return 1;
        }

        await api.CallAsync(HttpMethod.Delete, "/presets/" + preset.Id, ct);
        await terminal.WriteLineAsync("Removed \"" + preset.Name + "\"; chats started from it keep running.");
        return 0;
    }

    // The models an agent can run on with an account, newest first: those of the account's vendor that
    // can call tools, whose names or IDs hold the search when one is given.
    private async Task<int> ListModelsAsync(string[] words, CancellationToken ct)
    {
        CommandLine? line = CommandLine.Parse(words, ["--harness", "--account"], []);
        if (line is not { Arguments: [] or [_] })
        {
            return await UsageAsync();
        }

        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        AgentChoice? agent = await ChooseAgentAsync(api, host, workspace, line.Value("--account"), line.Value("--harness"), ct);
        if (agent is null)
        {
            return 1;
        }

        (Wire.Account account, Wire.Harness harness, _, _) = agent;
        string path = string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace}/models?harness={Uri.EscapeDataString(harness.Id)}&account={account.Id}");
        IReadOnlyList<Wire.Model> models = await api.GetAsync(path, CliJsonContext.Default.IReadOnlyListModel, ct);
        if (models.Count == 0)
        {
            await terminal.WriteLineAsync(harness.Name + " runs on its own default model with \"" + account.Name + "\".");
            return 0;
        }

        string search = line.Arguments is [string given] ? given : string.Empty;
        List<Wire.Model> matching = [.. models.Where(model => model.Id.Contains(search, StringComparison.OrdinalIgnoreCase) || model.Name.Contains(search, StringComparison.OrdinalIgnoreCase))];
        if (matching.Count == 0)
        {
            await terminal.FailAsync("No model of " + models.Count.ToString(CultureInfo.InvariantCulture) + " has \"" + search + "\" in its name or ID.");
            return 1;
        }

        int width = matching.Max(model => model.Id.Length);
        foreach (Wire.Model model in matching)
        {
            // OpenAI names its models by their IDs alone.
            string name = string.Equals(model.Name, model.Id, StringComparison.Ordinal) ? string.Empty : "  " + model.Name;
            await terminal.WriteLineAsync((model.Id.PadRight(width) + name).TrimEnd());
        }

        return 0;
    }

    // A preset of the person's in the workspace, by its name or ID, or null after saying which there are.
    private async Task<Wire.Preset?> FindPresetAsync(HostApi api, Guid workspace, string named, CancellationToken ct)
    {
        IReadOnlyList<Wire.Preset> presets = await api.GetAsync("/workspaces/" + workspace + "/presets", CliJsonContext.Default.IReadOnlyListPreset, ct);
        return await ChooseAsync(
            presets,
            named,
            last: null,
            found => found.Id.ToString("D", CultureInfo.InvariantCulture),
            found => found.Name,
            "preset",
            "its name",
            "You have no presets here.");
    }
}
