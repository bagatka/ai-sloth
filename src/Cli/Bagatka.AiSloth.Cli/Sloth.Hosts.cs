using System;
using System.Buffers.Text;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// Hosts work like git remotes: sloth signs in to each one it adds and uses one at a time. A host
// signs people in with a code, or through its identity provider in their browser; either way sloth
// ends with a session token of its own for this device.
internal sealed partial class Sloth
{
    // As long as a host waits for a browser to come back from its identity provider.
    private static readonly TimeSpan BrowserPatience = TimeSpan.FromMinutes(10);

    private async Task<int> AddHostAsync(string[] words, CancellationToken ct)
    {
        CommandLine? line = CommandLine.Parse(words, ["--code", "--name"], []);
        if (line is not { Arguments: [string address] })
        {
            return await UsageAsync();
        }

        bool parsed = Uri.TryCreate(address, UriKind.Absolute, out Uri? given);
        if (!parsed || given!.Scheme is not ("https" or "http") || given.PathAndQuery is not "/")
        {
            await terminal.FailAsync("A host's address is like https://sloth.example.com: http or https, without a path.");
            return 2;
        }

        Uri url = new Uri(given.GetLeftPart(UriPartial.Authority) + "/");
        using HostApi anonymous = new HostApi(http, url, token: null);
        Wire.HostDiscovery? discovery = await DiscoverAsync(anonymous, url, ct);
        if (discovery is null)
        {
            return 1;
        }

        string? code = line.Value("--code");
        Wire.SignedIn? signedIn;
        if (code is not null)
        {
            signedIn = await SignInWithCodeAsync(anonymous, code, line.Value("--name"), ct);
        }
        else if (discovery.SignIn.Provider is string provider)
        {
            signedIn = await SignInInBrowserAsync(anonymous, url, provider, ct);
        }
        else
        {
            await terminal.FailAsync(
                discovery.Name + " at " + url.Authority + " signs people in with codes. Ask someone on it for one, or use the setup code it printed when it first started: sloth host add "
                + url.AbsoluteUri.TrimEnd('/') + " --code <code>");
            return 1;
        }

        if (signedIn is null)
        {
            return 1;
        }

        return await RememberAsync(url, signedIn, ct);
    }

    // What the host is and how it signs people in, or null after saying it isn't a host this sloth knows.
    private async Task<Wire.HostDiscovery?> DiscoverAsync(HostApi anonymous, Uri url, CancellationToken ct)
    {
        using HttpResponseMessage found = await anonymous.SendAsync(HttpMethod.Get, "/.well-known/aisloth", content: null, ct);
        bool isHost = found.IsSuccessStatusCode && found.Content.Headers.ContentType?.MediaType is "application/json";
        if (!isHost)
        {
            await terminal.FailAsync(url.Authority + " isn't an AiSloth host: it has no /.well-known/aisloth.");
            return null;
        }

        Wire.HostDiscovery discovery = await anonymous.ReadAsync(found, CliJsonContext.Default.HostDiscovery, ct);
        if (discovery.ApiVersion != 1)
        {
            await terminal.FailAsync(url.Authority + " is a newer AiSloth than this sloth knows. Update sloth.");
            return null;
        }

        return discovery;
    }

    // Keeps the session, and makes the host the one commands use, with the workspace made for someone
    // new, else the person's first. Signing in again replaces the host's session here; the old one ends
    // after 90 days unused.
    private async Task<int> RememberAsync(Uri url, Wire.SignedIn signedIn, CancellationToken ct)
    {
        // Not handled: someone with more than 200 workspaces, whose later ones `workspace use` can't find.
        using HostApi api = new HostApi(http, url, signedIn.Token);
        Wire.WorkspacePage workspaces = await api.GetAsync("/workspaces?limit=200", CliJsonContext.Default.WorkspacePage, ct);
        Wire.Workspace? workspace = workspaces.Items.FirstOrDefault(found => found.Id == signedIn.Workspace) ?? (workspaces.Items is [Wire.Workspace first, ..] ? first : null);
        SignedInHost host = new SignedInHost(url.Authority, url, signedIn.Token, signedIn.Session, signedIn.User.Id, signedIn.User.Name, workspace?.Id, workspace?.Name, Defaults: null);
        HostsFile hosts = await ReadHostsAsync(ct);
        await PrivateFile.WriteAsync(HostsPath, hosts.With(host), CliJsonContext.Default.HostsFile, ct);
        await terminal.WriteLineAsync("Signed in to " + host.Name + " as " + host.UserName + ".");
        if (workspace is not null)
        {
            await terminal.WriteLineAsync("Commands use the workspace \"" + workspace.Name + "\"; sloth workspace use changes it.");
        }

        return 0;
    }

    // A setup code, a link code, or an invite where the host lets invites sign people up. Someone new
    // gives their name: sloth asks for it when the host says it's missing.
    private async Task<Wire.SignedIn?> SignInWithCodeAsync(HostApi anonymous, string code, string? name, CancellationToken ct)
    {
        using JsonContent request = JsonContent.Create(new Wire.CodeSignIn(code.Trim(), device, name), CliJsonContext.Default.CodeSignIn);
        using HttpResponseMessage response = await anonymous.SendAsync(HttpMethod.Post, "/sign-in/code", request, ct);
        bool mayAsk = response.StatusCode == HttpStatusCode.BadRequest && name is null && terminal.Interactive;
        if (mayAsk)
        {
            Wire.Problem problem = await HostApi.ProblemAsync(response, ct);
            bool needsName = problem.Errors?.ContainsKey("name") == true;
            if (needsName)
            {
                await terminal.WriteAsync("Your name: ");
                string? typed = await terminal.ReadLineAsync(ct);
                return await SignInWithCodeAsync(anonymous, code, typed ?? string.Empty, ct);
            }
        }

        return await anonymous.ReadAsync(response, CliJsonContext.Default.SignedIn, ct);
    }

    // OAuth's authorization code flow with PKCE against the host, which signs the person in with its
    // identity provider and sends the browser back here with a code for this sloth only.
    private async Task<Wire.SignedIn?> SignInInBrowserAsync(HostApi anonymous, Uri url, string provider, CancellationToken ct)
    {
        string verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        string state = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16));
        using LoopbackCallback callback = LoopbackCallback.Start();
        Uri start = new Uri(
            url,
            "/sign-in?redirect_uri=" + Uri.EscapeDataString(callback.Url.AbsoluteUri) + "&state=" + state + "&code_challenge=" + challenge
            + "&code_challenge_method=S256&device=" + Uri.EscapeDataString(device));
        Uri? returned = await BrowserRoundTripAsync(start, callback, provider, ct);
        if (returned is null)
        {
            return null;
        }

        string? code = LoopbackCallback.QueryValue(returned, "code");
        if (!string.Equals(LoopbackCallback.QueryValue(returned, "state"), state, StringComparison.Ordinal))
        {
            await terminal.FailAsync("The browser came back from another sign-in. Start again.");
            return null;
        }

        if (code is null)
        {
            bool refused = LoopbackCallback.QueryValue(returned, "error") is "access_denied";
            await terminal.FailAsync(refused ? provider + " didn't sign you in. Start again to retry." : url.Authority + " couldn't finish signing you in. Start again to retry.");
            return null;
        }

        return await anonymous.SendAsync(HttpMethod.Post, "/sign-in/token", new Wire.TokenExchange(code, verifier, callback.Url), CliJsonContext.Default.TokenExchange, CliJsonContext.Default.SignedIn, ct);
    }

    // Sends the person's browser to sign in somewhere and waits until it comes back, or null after
    // saying it didn't in time. The link is shown too, for a computer without a browser.
    private async Task<Uri?> BrowserRoundTripAsync(Uri start, LoopbackCallback callback, string signsInWith, CancellationToken ct)
    {
        await terminal.WriteLineAsync("Opening your browser to sign in with " + signsInWith + ". If it doesn't open, go to:");
        await terminal.WriteLineAsync("  " + start.AbsoluteUri);
        await openBrowser(start, ct);
        using CancellationTokenSource patience = new CancellationTokenSource(BrowserPatience, time);
        using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(ct, patience.Token);
        try
        {
            return await callback.WaitAsync(terminal, waiting.Token);
        }
        catch (OperationCanceledException) when (patience.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            await terminal.FailAsync("The browser didn't come back within 10 minutes. Start again.");
            return null;
        }
    }

    private async Task<int> ListHostsAsync(CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        if (hosts.Hosts.Count == 0)
        {
            await terminal.WriteLineAsync("You aren't signed in to a host. Sign in: sloth host add <url>");
            return 0;
        }

        int width = hosts.Hosts.Max(host => host.Name.Length);
        foreach (SignedInHost host in hosts.Hosts)
        {
            string marker = string.Equals(host.Name, hosts.Current, StringComparison.Ordinal) ? "* " : "  ";
            string workspace = host.WorkspaceName is null ? string.Empty : ", workspace \"" + host.WorkspaceName + "\"";
            await terminal.WriteLineAsync(marker + host.Name.PadRight(width) + "  " + host.UserName + workspace);
        }

        return 0;
    }

    private async Task<int> UseHostAsync(string name, CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = hosts.Find(name);
        if (host is null)
        {
            await terminal.FailAsync("You aren't signed in to " + name + ". Your hosts: sloth host list");
            return 1;
        }

        await PrivateFile.WriteAsync(HostsPath, hosts with { Current = host.Name }, CliJsonContext.Default.HostsFile, ct);
        await terminal.WriteLineAsync("Commands use " + host.Name + " now.");
        return 0;
    }

    private async Task<int> LinkDeviceAsync(CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        using HttpResponseMessage response = await api.SendAsync(HttpMethod.Post, "/users/me/link-codes", content: null, ct);
        Wire.LinkCode link = await api.ReadAsync(response, CliJsonContext.Default.LinkCode, ct);
        await terminal.WriteLineAsync("On your other device, within 10 minutes:");
        await terminal.WriteLineAsync("  sloth host add " + host.Url.AbsoluteUri.TrimEnd('/') + " --code " + link.Code);
        return 0;
    }

    // Ends this device's session on the host, then forgets the host. A host that can't be reached
    // keeps the session until it goes unused for 90 days.
    private async Task<int> RemoveHostAsync(string name, CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = hosts.Find(name);
        if (host is null)
        {
            await terminal.FailAsync("You aren't signed in to " + name + ". Your hosts: sloth host list");
            return 1;
        }

        using HostApi api = ApiFor(host);
        try
        {
            await api.CallAsync(HttpMethod.Delete, "/users/me/sessions/" + host.Session, ct);
        }
        catch (HttpRequestException exception) when (exception.StatusCode != HttpStatusCode.Unauthorized)
        {
            await terminal.WriteLineAsync("Couldn't end the session on " + host.Name + " (" + exception.Message + "); it ends after 90 days unused.");
        }

        await PrivateFile.WriteAsync(HostsPath, hosts.Without(host.Name), CliJsonContext.Default.HostsFile, ct);
        await terminal.WriteLineAsync("Signed out of " + host.Name + ".");
        return 0;
    }
}
