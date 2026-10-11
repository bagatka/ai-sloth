using System;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.WebApi;

internal static partial class Log
{
    [LoggerMessage(Level = LogLevel.Warning, Message = "PostHog lost {Events} events: {Reason}")]
    public static partial void PostHogEventsLost(ILogger logger, Exception? exception, int events, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "A model call passed the gateway but wasn't recorded: {Reason}")]
    public static partial void ModelCallNotRecorded(ILogger logger, string reason);
}
