using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// Instructions: what every agent of your chats is told to follow, whatever its harness: the
// workspace's for everyone's chats in it, and your own for the chats you start.
internal sealed partial class Sloth
{
    private async Task<int> ShowInstructionsAsync(CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.Instructions instructions = await api.GetAsync("/workspaces/" + workspace + "/instructions", CliJsonContext.Default.Instructions, ct);
        await terminal.WriteLineAsync("From \"" + host.WorkspaceName + "\", for everyone's chats there:");
        await terminal.WriteLineAsync(instructions.Workspace.Length > 0 ? instructions.Workspace.TrimEnd() : "(none)");
        await terminal.WriteLineAsync();
        await terminal.WriteLineAsync("Yours, for the chats you start:");
        await terminal.WriteLineAsync(instructions.Personal.Length > 0 ? instructions.Personal.TrimEnd() : "(none)");
        return 0;
    }

    // Sets yours, or the workspace's with --workspace, from a file, or from standard input with -.
    private async Task<int> SetInstructionsAsync(string[] words, CancellationToken ct)
    {
        CommandLine? line = CommandLine.Parse(words, [], ["--workspace"]);
        if (line is not { Arguments: [string source] })
        {
            return await UsageAsync();
        }

        string text;
        if (source is "-")
        {
            text = await terminal.ReadToEndAsync(ct);
        }
        else if (File.Exists(source))
        {
            text = await File.ReadAllTextAsync(source, ct);
        }
        else
        {
            await terminal.FailAsync("There's no file " + source + ". Give a file of instructions, or - to read them from standard input.");
            return 1;
        }

        return await StoreInstructionsAsync(line.Has("--workspace"), text, ct);
    }

    private async Task<int> ClearInstructionsAsync(string[] words, CancellationToken ct)
    {
        CommandLine? line = CommandLine.Parse(words, [], ["--workspace"]);
        if (line is not { Arguments: [] })
        {
            return await UsageAsync();
        }

        return await StoreInstructionsAsync(line.Has("--workspace"), string.Empty, ct);
    }

    private async Task<int> StoreInstructionsAsync(bool workspaceWide, string text, CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        string path = workspaceWide ? "/workspaces/" + workspace + "/instructions" : "/instructions";
        await api.CallAsync(HttpMethod.Put, path, new Wire.SetInstructions(text), CliJsonContext.Default.SetInstructions, ct);
        string whose = workspaceWide ? "\"" + host.WorkspaceName + "\"'s instructions" : "Your instructions";
        await terminal.WriteLineAsync(whose + (text.Length > 0 ? " are set" : " are cleared") + ": agents that start from now on follow them; running ones keep theirs.");
        return 0;
    }
}
