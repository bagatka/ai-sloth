using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Daemon;

/// <summary>
/// The daemon's log messages. They name processes and watches by ID only; commands, arguments, and
/// output never reach the log because they may hold secrets.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to the control plane")]
    public static partial void Connected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost the control plane connection; reconnecting in {RetryDelay}")]
    public static partial void ConnectionLost(ILogger logger, Exception? exception, TimeSpan retryDelay);

    [LoggerMessage(Level = LogLevel.Information, Message = "The control plane asked the daemon to reconnect")]
    public static partial void ReconnectRequested(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Started process {ProcessId}")]
    public static partial void ProcessStarted(ILogger logger, string processId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The disk is full: released the reserve so output and cleanup can continue")]
    public static partial void ReserveReleased(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Couldn't take the disk reserve; on a full disk, processes wait until space is freed")]
    public static partial void ReserveUnavailable(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Watch {WatchId} ended before the process's output was delivered")]
    public static partial void WatchEnded(ILogger logger, Exception exception, string watchId);
}
