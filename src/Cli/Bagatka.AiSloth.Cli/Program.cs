using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Cli;

// sloth's composition root: the only code that reads the environment. Everything else is Sloth's.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
Console.OutputEncoding = Encoding.UTF8;

// Ctrl+C and termination cancel the command, which ends cleanly; following a chat, Ctrl+C only leaves it.
using CancellationTokenSource shutdown = new CancellationTokenSource();
using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);

// sloth's folder is the operating system's per-user, non-roaming place for an app's own files: Linux's
// XDG config folder, macOS's Application Support, Windows' local application data, where a session's
// token never roams with the profile.
string folder = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "sloth")
    : Path.Combine(Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } config ? config : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"), "sloth");

Terminal terminal = new Terminal(Console.In, Console.Out, Console.Error, interactive: !Console.IsInputRedirected && !Console.IsOutputRedirected);
using SocketsHttpHandler http = new SocketsHttpHandler();
string? dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST") is { Length: > 0 } docker ? docker : null;
SlothBuild build = SlothBuild.Of(typeof(Sloth).Assembly, RuntimeInformation.RuntimeIdentifier, Environment.ProcessPath);

// DO_NOT_TRACK, the convention command-line tools share, set to anything but 0 turns usage reports off.
bool reportsUsage = Environment.GetEnvironmentVariable("DO_NOT_TRACK") is null or "" or "0";
Sloth sloth = new Sloth(terminal, folder, http, OpenBrowserAsync, "sloth on " + Environment.MachineName, dockerHost, build, reportsUsage, TimeProvider.System);
return await sloth.RunAsync(args, shutdown.Token);

// Opens a web page in the person's browser where this computer has one; sloth shows the link too.
static Task OpenBrowserAsync(Uri url, CancellationToken ct)
{
    if (url.Scheme is not ("https" or "http"))
    {
        return Task.CompletedTask;
    }

    ProcessStartInfo start = OperatingSystem.IsWindows() ? new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }
        : OperatingSystem.IsMacOS() ? new ProcessStartInfo("open", [url.AbsoluteUri])
        : new ProcessStartInfo("xdg-open", [url.AbsoluteUri]) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
    try
    {
        using Process? opened = Process.Start(start);
    }
    catch (Win32Exception)
    {
        // No browser here, such as over SSH: the person opens the link shown.
    }

    return Task.CompletedTask;
}

void Stop(PosixSignalContext context)
{
    context.Cancel = true;
    shutdown.Cancel();
}
