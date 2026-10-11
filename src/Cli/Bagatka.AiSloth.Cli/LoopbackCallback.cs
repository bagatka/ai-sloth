using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.AiSloth.Cli;

// Where a browser comes back to sloth after signing in somewhere: an HTTP listener on 127.0.0.1 at a
// free port until the browser arrives, or the address the browser ended on, pasted by the person when
// the browser runs on another computer, such as when sloth runs over SSH.
internal sealed class LoopbackCallback : IDisposable
{
    private const string CallbackPath = "/auth/callback";

    // How much of a request sloth reads: the request line and headers of a browser's GET.
    private const int MaxHeaderLines = 100;

    private readonly TcpListener _listener;

    private LoopbackCallback(TcpListener listener)
    {
        _listener = listener;
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Url = new Uri(string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port}{CallbackPath}"));
    }

    public Uri Url { get; }

    public static LoopbackCallback Start()
    {
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return new LoopbackCallback(listener);
    }

    // The whole address the browser came back to. Whichever way it arrives first wins; the other is
    // cancelled.
    public async Task<Uri> WaitAsync(Terminal terminal, CancellationToken ct)
    {
        if (!terminal.Interactive)
        {
            return await ReceiveAsync(ct);
        }

        using CancellationTokenSource done = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task<Uri> browser = ReceiveAsync(done.Token);
        Task<Uri> pasted = PastedAsync(terminal, done.Token);
        Task<Uri> first = await Task.WhenAny(browser, pasted);
        await done.CancelAsync();
        await ((Task)browser).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        return await first;
    }

    // The value of a query parameter of an address, or null without one.
    public static string? QueryValue(Uri url, string name)
    {
        foreach (string pair in url.Query.TrimStart('?').Split('&'))
        {
            int equals = pair.IndexOf('=', StringComparison.Ordinal);
            string key = equals < 0 ? pair : pair[..equals];
            if (string.Equals(Uri.UnescapeDataString(key), name, StringComparison.Ordinal))
            {
                return equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            }
        }

        return null;
    }

    public void Dispose()
    {
        _listener.Dispose();
    }

    private async Task<Uri> ReceiveAsync(CancellationToken ct)
    {
        while (true)
        {
            using TcpClient client = await _listener.AcceptTcpClientAsync(ct);
            await using NetworkStream stream = client.GetStream();
            string? target = await ReadTargetAsync(stream, ct);
            bool arrived = target is not null && target.StartsWith(CallbackPath + "?", StringComparison.Ordinal);
            string page = arrived ? "Done. You can close this tab and go back to the terminal." : "Not found.";
            string answer = string.Create(
                CultureInfo.InvariantCulture,
                $"HTTP/1.1 {(arrived ? "200 OK" : "404 Not Found")}\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: {Encoding.UTF8.GetByteCount(page)}\r\nConnection: close\r\n\r\n{page}");
            await stream.WriteAsync(Encoding.UTF8.GetBytes(answer), ct);
            if (arrived)
            {
                return new Uri(Url, target);
            }
        }
    }

    // The path and query of an HTTP request: `GET /auth/callback?code=… HTTP/1.1`.
    private static async Task<string?> ReadTargetAsync(NetworkStream stream, CancellationToken ct)
    {
        using StreamReader reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        string? requestLine = await reader.ReadLineAsync(ct);
        for (int read = 0; read < MaxHeaderLines; read++)
        {
            string? header = await reader.ReadLineAsync(ct);
            if (string.IsNullOrEmpty(header))
            {
                break;
            }
        }

        string[] parts = requestLine?.Split(' ') ?? [];
        return parts is ["GET", string target, _] ? target : null;
    }

    private static async Task<Uri> PastedAsync(Terminal terminal, CancellationToken ct)
    {
        await terminal.WriteLineAsync("If your browser is on another computer, paste the address it ends on here:");
        while (true)
        {
            string? line = await terminal.ReadLineAsync(ct);
            if (line is null)
            {
                // The end of input: only the browser can still come back.
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            bool parsed = Uri.TryCreate(line?.Trim(), UriKind.Absolute, out Uri? url);
            if (parsed && string.Equals(url!.AbsolutePath, CallbackPath, StringComparison.Ordinal))
            {
                return url;
            }

            await terminal.WriteLineAsync("That isn't it: the address starts with http://127.0.0.1 and has " + CallbackPath + " in it.");
        }
    }
}
