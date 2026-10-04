using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Nooks;

// The module's log messages. Nooks appear by ID only.
internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "Nook {NookId} failed: {Reason}")]
    public static partial void NookFailed(ILogger logger, Guid nookId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Deleted nook {NookId}")]
    public static partial void NookDeleted(ILogger logger, Guid nookId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciling nook {NookId} failed; the next pass retries")]
    public static partial void ReconcilingFailed(ILogger logger, Exception exception, Guid nookId);

    [LoggerMessage(Level = LogLevel.Error, Message = "A reconciler pass failed; the next pass retries")]
    public static partial void PassFailed(ILogger logger, Exception exception);
}
