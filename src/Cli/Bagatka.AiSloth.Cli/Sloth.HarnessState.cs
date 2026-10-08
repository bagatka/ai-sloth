using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// Harness state: what each harness's agents write for themselves to use later, such as Claude Code's
// memory, saved from the chats in the workspace in use and given to their next agents there: yours from
// the chats on your own accounts, and the workspace's shared one from the chats on its accounts.
internal sealed partial class Sloth
{
    private async Task<int> ListHarnessStatesAsync(CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        IReadOnlyList<Wire.HarnessState> states = await api.GetAsync("/workspaces/" + workspace + "/harness-state", CliJsonContext.Default.IReadOnlyListHarnessState, ct);
        if (states.Count == 0)
        {
            await terminal.WriteLineAsync("Nothing yet: what your agents keep for later, such as Claude Code's memory, is saved after each turn.");
            return 0;
        }

        foreach (Wire.HarnessState state in states)
        {
            string size = string.Create(CultureInfo.InvariantCulture, $"{(state.Bytes + 1023) / 1024} KiB");
            string from = state.SavedFrom is Guid chat ? " from chat " + ShortId(chat) : string.Empty;
            string whose = state.Shared ? "workspace's" : "yours";
            await terminal.WriteLineAsync(state.Harness.PadRight(12) + "  " + whose.PadRight(11) + "  " + size.PadLeft(10) + "  saved " + Ago(state.SavedAt) + from);
        }

        return 0;
    }

    private async Task<int> ForgetHarnessStateAsync(string harness, bool shared, CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        await api.CallAsync(HttpMethod.Delete, "/workspaces/" + workspace + "/harness-state/" + Uri.EscapeDataString(harness) + (shared ? "?shared=true" : string.Empty), ct);
        await terminal.WriteLineAsync(shared
            ? "Forgot the workspace's " + harness + " state: the next agents on its accounts start without it."
            : "Forgot your " + harness + " state in this workspace: your next agents on your own accounts start without it.");
        return 0;
    }
}
