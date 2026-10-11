using System;
using System.Net;
using System.Net.Http;
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
}
