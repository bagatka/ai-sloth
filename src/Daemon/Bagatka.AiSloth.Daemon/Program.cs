using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading;
using Bagatka.AiSloth.Daemon;
using Bagatka.PostHog;
using Microsoft.Extensions.Logging;

// The daemon's composition root: the only code that reads its environment, which the nook was
// created with.
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;

DaemonSettings settings;
try
{
    Uri? controlPlaneUrl = ParseUrl(Required("SLOTHD_CONTROL_PLANE_URL"));
    if (controlPlaneUrl is null)
    {
        throw Invalid("SLOTHD_CONTROL_PLANE_URL");
    }

    bool nookIdParsed = Guid.TryParse(Required("SLOTHD_NOOK_ID"), CultureInfo.InvariantCulture, out Guid nookId);
    if (!nookIdParsed)
    {
        throw Invalid("SLOTHD_NOOK_ID");
    }

    settings = new DaemonSettings(
        controlPlaneUrl,
        nookId,
        Required("SLOTHD_TOKEN"),
        Optional("SLOTHD_WORKING_DIRECTORY") ?? "/work",
        Optional("SLOTHD_STATE_DIRECTORY") ?? "/var/lib/slothd",
        OutputLimits.Default,
        NookDisk.DefaultReserveBytes);
}
catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
{
    // The nook was created with a wrong environment: one clear line, not a crash.
    await Console.Error.WriteLineAsync("slothd: " + exception.Message);
    return 2;
}

// The host's PostHog project, when it gave one, hears of a crash before the daemon exits, waiting two
// seconds at most to send it; nothing else is reported from here.
Uri? crashReportsHost = Optional("SLOTHD_POSTHOG_HOST") is string reportsTo ? ParseUrl(reportsTo) : null;
string? crashReportsToken = Optional("SLOTHD_POSTHOG_TOKEN");
await using PostHogClient? crashReports = crashReportsHost is not null && crashReportsToken is not null
    ? new PostHogClient(new PostHogClientOptions(crashReportsHost, crashReportsToken) { ShutdownTimeout = TimeSpan.FromSeconds(2) })
    : null;
bool crashReported = false;
AppDomain.CurrentDomain.UnhandledException += (_, unhandled) => ReportCrash(unhandled.ExceptionObject as Exception, flush: true);

using ILoggerFactory loggers = LoggerFactory.Create(logging => logging.AddSimpleConsole(console =>
{
    console.SingleLine = true;
    console.UseUtcTimestamp = true;
    console.TimestampFormat = "yyyy-MM-dd'T'HH':'mm':'ss'.'fff'Z' ";
}));

using CancellationTokenSource shutdown = new CancellationTokenSource();
using PosixSignalRegistration terminate = PosixSignalRegistration.Create(PosixSignal.SIGTERM, Stop);
using PosixSignalRegistration interrupt = PosixSignalRegistration.Create(PosixSignal.SIGINT, Stop);

await using SlothDaemon daemon = new SlothDaemon(settings, TimeProvider.System, loggers);
try
{
    await daemon.RunAsync(shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
    // Asked to stop; disposing the daemon kills its processes.
}
catch (Exception exception)
{
    // A crash goes on as one; disposing the client on the way out sends its report.
    ReportCrash(exception, flush: false);
    throw;
}

return 0;

// A crash for error tracking, as the daemon of its nook, once. One the runtime reports, of a thread
// the daemon doesn't await, is sent at once, as the process ends when this returns.
void ReportCrash(Exception? exception, bool flush)
{
    if (crashReports is null || exception is null || crashReported)
    {
        return;
    }

    crashReported = true;

    JsonObject properties = new JsonObject
    {
        ["$process_person_profile"] = false,
        ["nook_id"] = settings.NookId.ToString("D", CultureInfo.InvariantCulture),
        ["version"] = typeof(SlothDaemon).Assembly.GetName().Version?.ToString() ?? "0",
    };
    crashReports.CaptureException(exception, "slothd", properties);
    if (flush)
    {
        using CancellationTokenSource limit = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        crashReports.FlushAsync(limit.Token).GetAwaiter().GetResult();
    }
}

void Stop(PosixSignalContext context)
{
    context.Cancel = true;
    shutdown.Cancel();
}

static Uri? ParseUrl(string text)
{
    bool parsed = Uri.TryCreate(text, UriKind.Absolute, out Uri? url);
    return parsed ? url : null;
}

static string Required(string name)
{
    string? value = Optional(name);
    if (value is null)
    {
        throw new InvalidOperationException(name + " is not set.");
    }

    return value;
}

static InvalidOperationException Invalid(string name)
{
    return new InvalidOperationException(name + " is not valid.");
}

static string? Optional(string name)
{
    return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}
