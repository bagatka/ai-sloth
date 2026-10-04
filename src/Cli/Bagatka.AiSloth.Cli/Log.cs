using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Cli;

/// <summary>
/// The CLI's log messages. The machine's token never reaches the log.
/// </summary>
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "Connected to the control plane")]
    public static partial void Connected(ILogger logger);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Lost the control plane connection; reconnecting in {RetryDelay}")]
    public static partial void ConnectionLost(ILogger logger, Exception? exception, TimeSpan retryDelay);
}
