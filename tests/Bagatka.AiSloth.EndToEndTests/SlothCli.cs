using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Cli;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// <c>sloth</c> on one person's computer, run in this process: a folder of its own, input given as
/// text, output read as text, and a browser that follows redirects the way a person's does. At the
/// host's identity provider the browser signs in whoever <c>loginHint</c> names.
/// </summary>
internal sealed class SlothCli(string loginHint) : IAsyncDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("sloth-e2e-");
    private readonly SocketsHttpHandler _http = new SocketsHttpHandler();
    private readonly List<Task> _browsing = [];

    /// <summary>What the last command wrote to standard output.</summary>
    public string Output { get; private set; } = string.Empty;

    /// <summary>What the last command wrote to standard error.</summary>
    public string Errors { get; private set; } = string.Empty;

    /// <summary>Runs a command, with <paramref name="input"/> as its standard input, and returns its exit code.</summary>
    public async Task<int> RunWithInputAsync(string input, params string[] args)
    {
        using StringWriter output = new StringWriter();
        using StringWriter errors = new StringWriter();
        using StringReader reader = new StringReader(input);
        Terminal terminal = new Terminal(reader, output, errors, interactive: false);
        Sloth sloth = new Sloth(terminal, _folder.FullName, _http, Browse, "e2e", dockerHost: null, TimeProvider.System);
        int exit = await sloth.RunAsync(args, TestContext.Current.CancellationToken);
        Output = output.ToString();
        Errors = errors.ToString();
        return exit;
    }

    /// <summary>Runs a command without input.</summary>
    public async Task<int> RunAsync(params string[] args)
    {
        return await RunWithInputAsync(string.Empty, args);
    }

    /// <summary>The session token kept for a host, read as sloth reads it.</summary>
    public async Task<string?> TokenForAsync(string host)
    {
        HostsFile? hosts = await PrivateFile.ReadAsync(Path.Combine(_folder.FullName, "hosts.json"), CliJsonContext.Default.HostsFile, TestContext.Current.CancellationToken);
        return hosts?.Find(host)?.Token;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.WhenAll(_browsing);
        _http.Dispose();
        _folder.Delete(recursive: true);
    }

    // Like a person's browser, it opens the page and returns at once; the page goes on loading.
    private Task Browse(Uri url, CancellationToken ct)
    {
        _browsing.Add(FollowAsync(url, ct));
        return Task.CompletedTask;
    }

    private async Task FollowAsync(Uri url, CancellationToken ct)
    {
        Uri hinted = new Uri(url.AbsoluteUri + "&login_hint=" + Uri.EscapeDataString(loginHint));
        using HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = true, CheckCertificateRevocationList = true };
        using HttpClient browser = new HttpClient(handler);
        using HttpResponseMessage page = await browser.GetAsync(hinted, ct);
        page.EnsureSuccessStatusCode();
    }
}
