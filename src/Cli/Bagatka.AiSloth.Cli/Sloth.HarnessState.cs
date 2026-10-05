using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// Harness state: what each harness's agents write for themselves to use later, such as Claude Code's
// memory, saved from your chats in the workspace in use and given to their next agents there.
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
            await terminal.WriteLineAsync(state.Harness.PadRight(12) + "  " + size.PadLeft(10) + "  saved " + Ago(state.SavedAt) + " from chat " + ShortId(state.SavedFrom));
        }

        return 0;
    }

    private async Task<int> ForgetHarnessStateAsync(string harness, CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        await api.CallAsync(HttpMethod.Delete, "/workspaces/" + workspace + "/harness-state/" + Uri.EscapeDataString(harness), ct);
        await terminal.WriteLineAsync("Forgot your " + harness + " state in this workspace: your next agents here start without it.");
        return 0;
    }
}
