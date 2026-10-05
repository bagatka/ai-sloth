using System;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// sloth's commands, one area per file. They talk to the person through the terminal, call hosts through
// HostApi, and keep what lasts between runs in sloth's folder, readable by its owner only: the hosts
// signed in to (hosts.json) and a machine's credential (machine.json). A command returns its exit code:
// 0 when it did what it says, 1 when it couldn't, and 2 when it was called wrong.
internal sealed partial class Sloth(
    Terminal terminal,
    string folder,
    HttpMessageHandler http,
    Func<Uri, CancellationToken, Task> openBrowser,
    string device,
    string? dockerHost,
    TimeProvider time)
{
    private const string Usage = """
        Usage: sloth <command>

        Hosts
          sloth host add <url> [--code <code>] [--name <your name>]
                                     Sign in to a host: in your browser, or with a code
          sloth host list            The hosts you're signed in to; * marks the one in use
          sloth host use <host>      Use another host
          sloth host link            A code that signs you in on another device
          sloth host remove <host>   Sign out of a host

        Workspaces
          sloth workspace list
          sloth workspace use <name>
          sloth workspace invite read|write|manage
                                     A code that gives someone access to the workspace
          sloth workspace join <code>

        Agent accounts
          sloth account add          The kinds of account you can add
          sloth account add <kind> [--name <name>] [--endpoint <url>] [--workspace]
                                     Yours, or with --workspace the workspace's
          sloth account list
          sloth account remove <name>

        Secrets: environment variables for every process in the workspace's nooks
          sloth secret list
          sloth secret set <NAME>    Reads the value hidden, or from standard input
          sloth secret remove <NAME>

        Chats
          sloth chat "<message>" [--harness <id>] [--account <name>] [--on <provider>]
                                     Start a chat in a nook of its own, and follow it
          sloth chat list
          sloth chat open <id>       Follow a chat; type to write to the agent, /stop to stop it
          sloth chat send <id> "<message>"
          sloth chat stop <id>
          Ctrl+C only leaves a chat: the agent keeps working.

        Machines
          sloth machine connect <url> <code>
                                     Connect this computer to a workspace with a code from its owner
          sloth machine run          Run the workspace's nooks here, in Docker
        """;

    private string HostsPath => Path.Combine(folder, "hosts.json");

    public async Task<int> RunAsync(string[] args, CancellationToken ct)
    {
        Task<int> command = args switch
        {
            [] or ["help" or "--help" or "-h"] => HelpAsync(),
            ["host", "add", .. string[] rest] => AddHostAsync(rest, ct),
            ["host"] or ["host", "list"] => ListHostsAsync(ct),
            ["host", "use", string name] => UseHostAsync(name, ct),
            ["host", "link"] => LinkDeviceAsync(ct),
            ["host", "remove", string name] => RemoveHostAsync(name, ct),
            ["workspace"] or ["workspace", "list"] => ListWorkspacesAsync(ct),
            ["workspace", "use", string name] => UseWorkspaceAsync(name, ct),
            ["workspace", "invite", string access] => InviteAsync(access, ct),
            ["workspace", "join", string code] => JoinAsync(code, ct),
            ["account"] or ["account", "list"] => ListAccountsAsync(ct),
            ["account", "add"] => ListAccountKindsAsync(ct),
            ["account", "add", string kind, .. string[] rest] => AddAccountAsync(kind, rest, ct),
            ["account", "remove", string name] => RemoveAccountAsync(name, ct),
            ["secret"] or ["secret", "list"] => ListSecretsAsync(ct),
            ["secret", "set", string name] => SetSecretAsync(name, ct),
            ["secret", "remove", string name] => RemoveSecretAsync(name, ct),
            ["chat"] or ["chat", "list"] => ListChatsAsync(ct),
            ["chat", "open", string id] => OpenChatAsync(id, ct),
            ["chat", "send", string id, string text] => SendToChatAsync(id, text, ct),
            ["chat", "stop", string id] => StopChatAsync(id, ct),
            ["chat", "list" or "open" or "send" or "stop", ..] => UsageAsync(),
            ["chat", .. string[] rest] => StartChatAsync(rest, ct),
            ["machine", "connect", string url, string code] => ConnectMachineAsync(url, code, ct),
            ["machine", "run"] => RunMachineAsync(ct),
            _ => UsageAsync(),
        };

        try
        {
            return await command;
        }
        catch (HttpRequestException exception)
        {
            await terminal.FailAsync(exception.Message);
            return 1;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Ctrl+C, as shells count it.
            return 130;
        }
    }

    private async Task<int> HelpAsync()
    {
        await terminal.WriteLineAsync(Usage);
        return 0;
    }

    private async Task<int> UsageAsync()
    {
        await terminal.FailAsync("That isn't a command sloth knows.");
        await terminal.WriteLineAsync(Usage);
        return 2;
    }

    private async Task<HostsFile> ReadHostsAsync(CancellationToken ct)
    {
        // Not handled: a hosts file damaged outside sloth, which always writes it whole; its
        // JsonException names the file.
        HostsFile? hosts = await PrivateFile.ReadAsync(HostsPath, CliJsonContext.Default.HostsFile, ct);
        return hosts ?? HostsFile.Empty;
    }

    // The host commands use, or null after saying how to sign in to one.
    private async Task<SignedInHost?> CurrentHostAsync(HostsFile hosts)
    {
        SignedInHost? host = hosts.CurrentHost();
        if (host is null)
        {
            await terminal.FailAsync("You aren't signed in to a host. Sign in: sloth host add <url>");
        }

        return host;
    }

    // The current host and the workspace commands use on it, or null after saying what's missing.
    private async Task<(SignedInHost Host, Guid Workspace)?> WorkspaceInUseAsync(CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return null;
        }

        if (host.Workspace is not Guid workspace)
        {
            await terminal.FailAsync(NoWorkspace(host));
            return null;
        }

        return (host, workspace);
    }

    private static string NoWorkspace(SignedInHost host)
    {
        return "No workspace is in use on " + host.Name + ". Choose one: sloth workspace use <name>";
    }

    private HostApi ApiFor(SignedInHost host)
    {
        return new HostApi(http, host.Url, host.Token);
    }

    // How long ago, for people: "just now", "5 min ago", "3 h ago", "2 d ago".
    private string Ago(DateTimeOffset at)
    {
        TimeSpan age = time.GetUtcNow() - at;
        return age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalMinutes} min ago")
            : age.TotalDays < 1 ? string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalHours} h ago")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)age.TotalDays} d ago");
    }
}
