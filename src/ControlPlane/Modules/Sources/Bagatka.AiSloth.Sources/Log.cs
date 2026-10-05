using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Sources;

// The module's log messages. People appear by ID only; tokens and codes never reach the log.
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Information, Message = "GitHub ended the connection of user {UserId}: {Reason}")]
    public static partial void ConnectionEnded(ILogger logger, Guid userId, string reason);
}
