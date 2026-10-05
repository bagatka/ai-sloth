using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// Agent accounts: what pays for agents' work. A person's own, or with --workspace the workspace's,
// which everyone with Write on it uses. Secrets are typed hidden or read from standard input, and plans
// added by signing in go through the person's browser; neither is ever shown again.
internal sealed partial class Sloth
{
    private async Task<int> ListAccountsAsync(CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        IReadOnlyList<Wire.Account> accounts = await api.GetAsync("/workspaces/" + workspace + "/agent-accounts", CliJsonContext.Default.IReadOnlyListAccount, ct);
        if (accounts.Count == 0)
        {
            await terminal.WriteLineAsync("No agent accounts yet. Add one: sloth account add");
            return 0;
        }

        int width = accounts.Max(account => account.Name.Length);
        foreach (Wire.Account account in accounts)
        {
            string kind = AccountKinds.FromHost(account.Kind)?.Id ?? account.Kind;
            string whose = account.WorkspaceId is null ? "yours" : "the workspace's";
            string endpoint = account.Endpoint is null ? string.Empty : "  " + account.Endpoint;
            string state = account.NeedsSignIn ? "  signed out: remove it and add it again" : string.Empty;
            await terminal.WriteLineAsync(account.Name.PadRight(width) + "  " + kind.PadRight(18) + "  " + whose + endpoint + state);
        }

        return 0;
    }

    // Every kind the host knows, how each is added, whether the host allows it, and which harnesses
    // run on it.
    private async Task<int> ListAccountKindsAsync(CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        IReadOnlyList<Wire.AccountKind> offered = await api.GetAsync("/agent-account-kinds", CliJsonContext.Default.IReadOnlyListAccountKind, ct);
        IReadOnlyList<Wire.Harness> harnesses = await api.GetAsync("/harnesses", CliJsonContext.Default.IReadOnlyListHarness, ct);
        await terminal.WriteLineAsync("Add an account with: sloth account add <kind>");
        foreach (bool plans in (bool[])[true, false])
        {
            await terminal.WriteLineAsync();
            await terminal.WriteLineAsync(plans ? "Plans: the subscription you already pay for, yours only" : "API keys: billed per token; --endpoint for another endpoint that speaks the API");
            foreach (Wire.AccountKind kind in offered)
            {
                AccountKinds.Kind? known = AccountKinds.FromHost(kind.Kind);
                if ((known?.IsPlan ?? kind.PersonalOnly) != plans)
                {
                    continue;
                }

                string how = !kind.Allowed ? "not allowed on " + host.Name
                    : kind.AddedBySignIn ? "sign in with " + (known?.SignsInWith ?? "its vendor")
                    : (known?.SecretPrompt ?? "its secret");
                string runs = string.Join(", ", harnesses.Where(harness => harness.Accepts.Contains(kind.Kind, StringComparer.Ordinal)).Select(harness => harness.Name));
                await terminal.WriteLineAsync(
                    "  " + (known?.Id ?? kind.Kind).PadRight(18) + "  " + (known?.Description ?? string.Empty).PadRight(46) + "  " + how.PadRight(30) + "  " + runs);
            }
        }

        return 0;
    }

    private async Task<int> AddAccountAsync(string kindId, string[] words, CancellationToken ct)
    {
        CommandLine? line = CommandLine.Parse(words, ["--name", "--endpoint"], ["--workspace"]);
        AccountKinds.Kind? kind = AccountKinds.Find(kindId);
        if (line is not { Arguments: [] } || kind is null)
        {
            await terminal.FailAsync(kind is null ? "There's no kind of account \"" + kindId + "\". See the kinds: sloth account add" : "That isn't how to add an account.");
            return 2;
        }

        string? endpointText = line.Value("--endpoint");
        bool parsed = Uri.TryCreate(endpointText, UriKind.Absolute, out Uri? endpoint);
        if (parsed != (endpointText is not null) || endpoint?.Scheme is not (null or "https" or "http"))
        {
            await terminal.FailAsync("An endpoint is the API's base URL, such as https://openrouter.ai/api/v1.");
            return 2;
        }

        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        bool forWorkspace = line.Has("--workspace");
        if (forWorkspace && host.Workspace is null)
        {
            await terminal.FailAsync(NoWorkspace(host));
            return 1;
        }

        using HostApi api = ApiFor(host);
        Wire.AccountKind? rules = await AllowedAsync(api, host, kind, ct);
        if (rules is null)
        {
            return 1;
        }

        string name = line.Value("--name") ?? kind.Name;
        string path = forWorkspace ? "/workspaces/" + host.Workspace + "/agent-accounts" : "/agent-accounts";
        Wire.Account? account;
        if (rules.AddedBySignIn)
        {
            account = await SignInAccountAsync(api, kind, name, ct);
        }
        else
        {
            account = await AddWithSecretAsync(api, path, kind, name, endpoint, ct);
        }

        if (account is null)
        {
            return 1;
        }

        await terminal.WriteLineAsync("Added \"" + account.Name + "\": " + (account.WorkspaceId is null ? "yours." : "the workspace's."));
        return 0;
    }

    // How the host adds the kind, or null after saying it doesn't allow it.
    private async Task<Wire.AccountKind?> AllowedAsync(HostApi api, SignedInHost host, AccountKinds.Kind kind, CancellationToken ct)
    {
        IReadOnlyList<Wire.AccountKind> offered = await api.GetAsync("/agent-account-kinds", CliJsonContext.Default.IReadOnlyListAccountKind, ct);
        Wire.AccountKind? rules = offered.FirstOrDefault(found => string.Equals(found.Kind, kind.HostKind, StringComparison.Ordinal));
        if (rules is null || !rules.Allowed)
        {
            await terminal.FailAsync(host.Name + " doesn't allow " + kind.Name + "s. See what it allows: sloth account add");
            return null;
        }

        return rules;
    }

    private async Task<Wire.Account?> AddWithSecretAsync(HostApi api, string path, AccountKinds.Kind kind, string name, Uri? endpoint, CancellationToken ct)
    {
        string? secret = await terminal.ReadSecretAsync(kind.SecretPrompt ?? "Secret", ct);
        Wire.AddAccount add = new Wire.AddAccount(kind.HostKind, name, secret?.Trim() ?? string.Empty, endpoint);
        return await api.SendAsync(HttpMethod.Post, path, add, CliJsonContext.Default.AddAccount, CliJsonContext.Default.Account, ct);
    }

    // A plan added by signing in at its vendor in the person's browser, which comes back to sloth;
    // the host finishes the sign-in with the address it came back to.
    private async Task<Wire.Account?> SignInAccountAsync(HostApi api, AccountKinds.Kind kind, string name, CancellationToken ct)
    {
        using LoopbackCallback callback = LoopbackCallback.Start();
        Wire.AccountSignInStarted started = await api.SendAsync(
            HttpMethod.Post,
            "/agent-accounts/sign-ins",
            new Wire.StartAccountSignIn(kind.HostKind, name, callback.Url),
            CliJsonContext.Default.StartAccountSignIn,
            CliJsonContext.Default.AccountSignInStarted,
            ct);
        Uri? returned = await BrowserRoundTripAsync(started.Url, callback, kind.SignsInWith ?? kind.Name, ct);
        if (returned is null)
        {
            return null;
        }

        return await api.SendAsync(
            HttpMethod.Post,
            "/agent-accounts/sign-ins/" + started.Id + "/complete",
            new Wire.CompleteAccountSignIn(returned),
            CliJsonContext.Default.CompleteAccountSignIn,
            CliJsonContext.Default.Account,
            ct);
    }

    private async Task<int> RemoveAccountAsync(string name, CancellationToken ct)
    {
        (SignedInHost Host, Guid Workspace)? inUse = await WorkspaceInUseAsync(ct);
        if (inUse is not (SignedInHost host, Guid workspace))
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        IReadOnlyList<Wire.Account> accounts = await api.GetAsync("/workspaces/" + workspace + "/agent-accounts", CliJsonContext.Default.IReadOnlyListAccount, ct);
        List<Wire.Account> named = [.. accounts.Where(account => Names(account.Id, account.Name, name))];
        if (named is not [Wire.Account account])
        {
            await terminal.FailAsync(named.Count == 0
                ? "There's no account \"" + name + "\" here. The accounts: sloth account list"
                : "Several accounts are named \"" + name + "\"; use one's ID: " + string.Join(", ", named.Select(found => found.Id)));
            return 1;
        }

        await api.CallAsync(HttpMethod.Delete, "/agent-accounts/" + account.Id, ct);
        await terminal.WriteLineAsync("Removed \"" + account.Name + "\"; agents running on it stop at once.");
        return 0;
    }
}
