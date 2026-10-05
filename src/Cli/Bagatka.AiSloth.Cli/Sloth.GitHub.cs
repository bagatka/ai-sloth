using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// GitHub: each person connects their account through the host's GitHub App, so AiSloth copies their
// repositories in and pushes their changes as them. A host's operator makes its app once.
internal sealed partial class Sloth
{
    // The least a wait between asking whether the person approved yet lasts.
    private static readonly TimeSpan MinPollWait = TimeSpan.FromSeconds(1);

    private async Task<int> ConnectGitHubAsync(CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        using HttpResponseMessage response = await api.SendAsync(HttpMethod.Post, "/github/connections", content: null, ct);
        Wire.GitHubConnectionStarted started = await api.ReadAsync(response, CliJsonContext.Default.GitHubConnectionStarted, ct);
        await terminal.WriteLineAsync("Open " + started.VerificationUri + " and enter " + started.UserCode + " to connect your GitHub account.");
        await openBrowser(started.VerificationUri, ct);

        TimeSpan wait = started.Interval;
        while (time.GetUtcNow() < started.ExpiresAt)
        {
            await Task.Delay(wait > MinPollWait ? wait : MinPollWait, time, ct);
            using HttpResponseMessage asked = await api.SendAsync(HttpMethod.Post, "/github/connections/" + started.Id + "/complete", content: null, ct);
            Wire.GitHubConnectionProgress progress = await api.ReadAsync(asked, CliJsonContext.Default.GitHubConnectionProgress, ct);
            if (progress.Account is not null)
            {
                await terminal.WriteLineAsync("Connected GitHub as " + progress.Account.Login + ". Add a repository: sloth repo add");
                return 0;
            }

            wait = progress.RetryAfter;
        }

        await terminal.FailAsync("The code expired before GitHub said you approved. Start again: sloth github connect");
        return 1;
    }

    private async Task<int> ShowGitHubAsync(CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        using HttpResponseMessage response = await api.SendAsync(HttpMethod.Get, "/github/account", content: null, ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            await terminal.WriteLineAsync("Your GitHub account isn't connected to " + host.Name + ". Connect it: sloth github connect");
            return 0;
        }

        Wire.GitHubAccount account = await api.ReadAsync(response, CliJsonContext.Default.GitHubAccount, ct);
        await terminal.WriteLineAsync("Connected to GitHub as " + account.Login + ".");
        return 0;
    }

    private async Task<int> DisconnectGitHubAsync(CancellationToken ct)
    {
        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        using HostApi api = ApiFor(host);
        await api.CallAsync(HttpMethod.Delete, "/github/account", ct);
        await terminal.WriteLineAsync("Disconnected GitHub from " + host.Name + ". GitHub still lists the app among your authorized apps until you revoke it there.");
        return 0;
    }

    // Makes the host's GitHub App with GitHub's manifest flow: a page here posts its description to
    // GitHub, the operator confirms, and GitHub sends the browser back with a code for the app's keys.
    private async Task<int> CreateGitHubAppAsync(string[] words, CancellationToken ct)
    {
        CommandLine? line = CommandLine.Parse(words, ["--org", "--name"], ["--public"]);
        if (line is not { Arguments: [] })
        {
            return await UsageAsync();
        }

        HostsFile hosts = await ReadHostsAsync(ct);
        SignedInHost? host = await CurrentHostAsync(hosts);
        if (host is null)
        {
            return 1;
        }

        string state = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string? organization = line.Value("--org");
        Uri gitHub = new Uri("https://github.com/");
        Uri form = new Uri(gitHub, (organization is null ? "settings/apps/new" : "organizations/" + Uri.EscapeDataString(organization) + "/settings/apps/new") + "?state=" + state);
        string name = line.Value("--name") ?? "AiSloth on " + host.Name;
        using LoopbackCallback callback = LoopbackCallback.Start(back => ManifestPage(form, Manifest(name, host.Url, back, line.Has("--public"))));
        Uri? returned = await BrowserRoundTripAsync(callback.StartUrl, callback, "GitHub", ct);
        if (returned is null)
        {
            return 1;
        }

        string? code = LoopbackCallback.QueryValue(returned, "code");
        if (code is null || !string.Equals(LoopbackCallback.QueryValue(returned, "state"), state, StringComparison.Ordinal))
        {
            await terminal.FailAsync("GitHub didn't make the app. Start again.");
            return 1;
        }

        using HttpClient client = new HttpClient(http, disposeHandler: false);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("sloth");
        using HttpResponseMessage converted = await client.PostAsync(new Uri("https://api.github.com/app-manifests/" + Uri.EscapeDataString(code) + "/conversions"), content: null, ct);
        converted.EnsureSuccessStatusCode();
        GitHubAppManifest.Conversion? app = await converted.Content.ReadFromJsonAsync(GitHubJsonContext.Default.Conversion, ct);
        if (app is null)
        {
            await terminal.FailAsync("GitHub made the app but sent no keys. Make a client secret in its settings: https://github.com/settings/apps");
            return 1;
        }

        await PrintAppSettingsAsync(app);
        return 0;
    }

    private async Task PrintAppSettingsAsync(GitHubAppManifest.Conversion app)
    {
        await terminal.WriteLineAsync("Made the GitHub App " + app.HtmlUrl + ".");
        await terminal.WriteLineAsync("One more step at GitHub: in its settings, check \"Enable Device Flow\" and save.");
        await terminal.WriteLineAsync("Then give the host these settings, as environment variables:");
        await terminal.WriteLineAsync("  Modules__Sources__GitHubApp__ClientId=" + app.ClientId);
        await terminal.WriteLineAsync("  Modules__Sources__GitHubApp__ClientSecret=" + app.ClientSecret);
        await terminal.WriteLineAsync("  Modules__Sources__GitHubApp__Slug=" + app.Slug);
        await terminal.WriteLineAsync("For a local run, as the AppHost's user secrets: Parameters:github-app-client-id, -client-secret, and -slug.");
    }

    // The app: it acts as people who connect it, reading and writing contents and pull requests.
    private static string Manifest(string name, Uri host, Uri redirect, bool isPublic)
    {
        GitHubAppManifest.Manifest manifest = new GitHubAppManifest.Manifest(
            name,
            host,
            new GitHubAppManifest.Hook(new Uri(host, "github/events"), Active: false),
            redirect,
            isPublic,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["contents"] = "write", ["pull_requests"] = "write", ["metadata"] = "read" },
            RequestOauthOnInstall: false);
        return JsonSerializer.Serialize(manifest, GitHubJsonContext.Default.Manifest);
    }

    // Posts the manifest to GitHub as soon as the browser opens it: the flow starts with a form post.
    private static string ManifestPage(Uri form, string manifest)
    {
        string encoded = WebUtility.HtmlEncode(manifest);
        return "<!doctype html><meta charset=\"utf-8\"><title>Make the GitHub App</title>"
            + "<form method=\"post\" action=\"" + WebUtility.HtmlEncode(form.AbsoluteUri) + "\">"
            + "<input type=\"hidden\" name=\"manifest\" value=\"" + encoded + "\">"
            + "<button>Continue to GitHub</button></form><script>document.forms[0].submit()</script>";
    }
}
