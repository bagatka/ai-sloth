using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Bagatka.AiSloth.Daemon;
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

return 0;

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
